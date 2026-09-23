// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;

namespace DDT.Agent;

// Registers the machine, waits until it may run a task sequence, runs it, and after a restart in the middle of a run
// goes on with the run whose state it finds on the disk, as long as the server still runs it. A start that finds a
// restart still due makes that restart first, without registering.
public sealed class AgentLoop(
    IAgentServer server,
    IMachineIdentityReader identityReader,
    ISignInPrompt prompt,
    IDiskPartitioner disks,
    SequenceRunner runner,
    LocalRunLocator locator,
    AgentLog log,
    TimeProvider timeProvider,
    string agentVersion)
{
    private MachineIdentity? _lastIdentity;
    private string? _resumeToken;

    // The run an earlier start of the agent left on the disk, until it goes on or the server ended it.
    private LocalRun? _localRun;

    // The newest run token: from the disk, a registration or the run itself.
    private string? _runToken;

    // A run that ended in this process without the server hearing so, and the Failed report that tells it.
    private (Guid RunId, AgentRunReport Report)? _abandonedRun;

    // The disk last confirmed with ERASE in this process. Kept past the pick's answer: a pick the server stored but
    // did not confirm still arrives as an Assigned run.
    private LocalDisk? _confirmedDisk;

    // The run last chosen at this console without a disk confirmed with ERASE, which therefore must not erase one.
    private Guid? _pickedWithoutErase;

    // The disks the picker offers, read once each time the machine may pick; null until then.
    private IReadOnlyList<LocalDisk>? _pickableDisks;
    private bool _toldNoSequences;
    private bool _toldNoDeployments;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        log.Information($"DDT agent {agentVersion}");

        if (await runner.RestartIfDueAsync(cancellationToken).ConfigureAwait(false) is { } restarted)
        {
            return restarted == RunOutcome.Restarting ? AgentExitCodes.Restarting : AgentExitCodes.Stopped;
        }

        if (!prompt.IsAvailable)
        {
            log.Information("Nobody can type at this console. Unless the server requires a sign in at the machine, approve it on the Machines page.");
        }

        bool first = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            // After a refused token, never register again in a tight loop: two agents fighting over one
            // machine would otherwise hammer the server.
            if (!first && !await DelayAsync(AgentLimits.MinRetryDelay, cancellationToken).ConfigureAwait(false))
            {
                return AgentExitCodes.Stopped;
            }

            first = false;

            try
            {
                await FindLocalRunAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return AgentExitCodes.Stopped;
            }

            AgentRegistrationResult? registration = await RegisterAsync(cancellationToken).ConfigureAwait(false);

            if (registration is null)
            {
                return AgentExitCodes.Stopped;
            }

            KeepOrDiscardLocalRun(registration);

            if (registration.Token is null)
            {
                log.Error($"An administrator rejected machine {registration.MachineId}. The agent stops here.");

                return AgentExitCodes.Rejected;
            }

            _resumeToken = registration.ResumeToken;
            log.Information($"Registered as machine {registration.MachineId}, {Describe(registration.State)}");

            int? exitCode = await PollAsync(registration, cancellationToken).ConfigureAwait(false);

            if (exitCode is { } code)
            {
                return code;
            }
        }

        return AgentExitCodes.Stopped;
    }

    // Returns null to register again, or an exit code. Requests stay on this loop, in order: only reading the
    // keyboard runs alongside polling, and a run is awaited here, with its own heartbeat instead of polls.
    private async Task<int?> PollAsync(AgentRegistrationResult registration, CancellationToken cancellationToken)
    {
        Guid machineId = registration.MachineId;
        string token = registration.Token!;
        MachineState state = registration.State;
        string? signedInBy = registration.SignedInBy;
        TimeSpan interval = TimeSpan.FromSeconds(registration.PollAfterSeconds);
        int failures = 0;

        SignInConversation conversation = new(prompt, log);
        SequencePicker picker = new(prompt, log);
        CancellationTokenSource? stopTyping = null;
        Task<string?>? typing = null;
        bool typingForPicker = false;
        _pickableDisks = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    AgentNextResult next = await server.NextAsync(machineId, token, cancellationToken).ConfigureAwait(false);

                    failures = 0;
                    interval = TimeSpan.FromSeconds(next.PollAfterSeconds);
                    token = next.Token;
                    _resumeToken = next.ResumeToken;

                    if (next.State != state)
                    {
                        state = next.State;
                        log.Information($"Machine is now {Describe(state)}");
                    }

                    if (next.SignedInBy != signedInBy)
                    {
                        signedInBy = next.SignedInBy;

                        if (state == MachineState.Pending && signedInBy is not null)
                        {
                            log.Information($"{signedInBy} signed in at this machine. An operator still has to approve it on the Machines page.");
                        }
                    }

                    bool authorized = state is MachineState.Approved or MachineState.Deploying or MachineState.Failed;
                    AgentRun? run = authorized ? next.Run : null;

                    if (authorized && next.Deployment is not null && next.Run is null && !_toldNoDeployments)
                    {
                        _toldNoDeployments = true;
                        log.Warning("The server assigned an image deployment, which this agent no longer runs. Assign a task sequence instead.");
                    }

                    if (run is { State: DeploymentState.Assigned or DeploymentState.Running } && typing is not null)
                    {
                        await StopTypingAsync(stopTyping!, typing).ConfigureAwait(false);
                        typing = null;
                    }

                    LocalRun? resumable = run is { State: DeploymentState.Running } && _localRun is { } local && local.State.RunId == run.Id
                        ? local
                        : null;

                    if (run is { State: DeploymentState.Assigned } && run.Id == _pickedWithoutErase && run.Sequence.Steps.Any(step => step.ErasesDisk))
                    {
                        // The picker said why when the server answered the choice.
                        AgentRunReportResult reported = await server
                            .ReportRunAsync(machineId, token, run.Id, FailedBeforeItRan(SequencePicker.ChangedAfterChoiceMessage), cancellationToken)
                            .ConfigureAwait(false);

                        token = reported.Token;
                        _resumeToken = reported.ResumeToken;
                        _pickedWithoutErase = null;
                    }
                    else if (run is { State: DeploymentState.Assigned } || resumable is not null)
                    {
                        _localRun = null;
                        DeploymentTokens tokens = new(token, next.ResumeToken, _runToken);
                        RunResult result = await runner
                            .RunAsync(machineId, run!, resumable, resumable is null ? _confirmedDisk : null, tokens, _lastIdentity!, cancellationToken)
                            .ConfigureAwait(false);

                        _abandonedRun = result.UnsentReport is { } unsent ? (run!.Id, unsent) : null;

                        // The run kept the session alive; the tokens this loop last saw may have expired.
                        token = tokens.Token;
                        _resumeToken = tokens.ResumeToken;
                        _runToken = tokens.RunToken;

                        switch (result.Outcome)
                        {
                            case RunOutcome.Finished:
                                return AgentExitCodes.Deployed;
                            case RunOutcome.Restarting:
                                return AgentExitCodes.Restarting;
                            case RunOutcome.Stopped:
                                return AgentExitCodes.Stopped;
                            case RunOutcome.TokenRejected:
                                return null;
                            default:
                                _runToken = null;
                                picker.Reset();
                                _pickableDisks = null;

                                continue;
                        }
                    }
                    else if (run is { State: DeploymentState.Running })
                    {
                        // Nothing here can go on with it: its state is not on this machine's disks, a refused token
                        // ended it, or its own failure report did not get through.
                        AgentRunReport report = _abandonedRun is { } abandoned && abandoned.RunId == run.Id
                            ? abandoned.Report
                            : FailedBeforeItRan(SequenceRunner.LostContactMessage);

                        log.Error(report.Error!);
                        AgentRunReportResult reported = await server.ReportRunAsync(machineId, token, run.Id, report, cancellationToken)
                            .ConfigureAwait(false);

                        token = reported.Token;
                        _resumeToken = reported.ResumeToken;
                        _abandonedRun = null;
                        _runToken = null;
                    }

                    bool signInWanted = state == MachineState.Pending && signedInBy is null && conversation.IsAvailable;
                    bool pickWanted = next.CanPickSequence && run is null && picker.IsAvailable;

                    if (!pickWanted)
                    {
                        picker.Reset();
                        _pickableDisks = null;
                    }
                    else if (!picker.IsOffered)
                    {
                        await OfferSequencesAsync(picker, machineId, token, cancellationToken).ConfigureAwait(false);
                    }

                    if (typing is not null && (typingForPicker ? !picker.IsOffered : !signInWanted))
                    {
                        await StopTypingAsync(stopTyping!, typing).ConfigureAwait(false);
                        typing = null;
                    }

                    if (typing is null && (signInWanted || picker.IsOffered))
                    {
                        typingForPicker = !signInWanted;
                        stopTyping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        typing = typingForPicker ? picker.ReadAsync(stopTyping.Token) : conversation.ReadAsync(stopTyping.Token);
                    }

                    // Only an authorized machine may write to the server's log. Until then lines wait here.
                    if (authorized)
                    {
                        await FlushAsync(machineId, token, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (AgentTokenRejectedException)
                {
                    log.Warning("The server no longer accepts this machine's token. Registering again.");

                    return null;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return AgentExitCodes.Stopped;
                }
                catch (Exception exception) when (IsTransient(exception))
                {
                    failures++;
                    interval = AgentLimits.RetryDelay(failures);
                    log.Warning($"Cannot reach the server ({exception.Message}). Retrying in {interval.TotalSeconds:0} s.");
                }

                if (typing is null)
                {
                    if (!await DelayAsync(interval, cancellationToken).ConfigureAwait(false))
                    {
                        return AgentExitCodes.Stopped;
                    }

                    continue;
                }

                using (CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    Task elapsed = Task.Delay(interval, timeProvider, waiting.Token);

                    if (await Task.WhenAny(typing, elapsed).ConfigureAwait(false) != typing)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return AgentExitCodes.Stopped;
                        }

                        continue;
                    }

                    await waiting.CancelAsync().ConfigureAwait(false);
                }

                string? typed = await typing.ConfigureAwait(false);
                stopTyping!.Dispose();
                stopTyping = null;
                typing = null;

                // Poll before asking for the next field, so an approval or an assignment on the web is noticed
                // right away.
                if (typed is null)
                {
                    continue;
                }

                try
                {
                    if (typingForPicker)
                    {
                        await SendPickAsync(picker, machineId, token, typed, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await SendSignInAsync(conversation, machineId, token, typed, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (AgentTokenRejectedException)
                {
                    log.Warning("The server no longer accepts this machine's token. Registering again.");

                    return null;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return AgentExitCodes.Stopped;
                }
            }

            return AgentExitCodes.Stopped;
        }
        finally
        {
            if (typing is not null)
            {
                await StopTypingAsync(stopTyping!, typing).ConfigureAwait(false);
            }
        }
    }

    // A refused token and a stop reach the caller.
    private async Task SendSignInAsync(
        SignInConversation conversation,
        Guid machineId,
        string token,
        string typed,
        CancellationToken cancellationToken)
    {
        if (conversation.Accept(typed) is not { } request)
        {
            return;
        }

        try
        {
            AgentSignInResult result = await server.SignInAsync(machineId, token, request, cancellationToken).ConfigureAwait(false);
            conversation.Handle(result.Status);
        }
        catch (Exception exception) when (IsTransient(exception) && !cancellationToken.IsCancellationRequested)
        {
            conversation.NotSent(exception);
        }
    }

    // A refused token and a stop reach the caller.
    private async Task SendPickAsync(SequencePicker picker, Guid machineId, string token, string typed, CancellationToken cancellationToken)
    {
        if (picker.Accept(typed) is not { } request)
        {
            return;
        }

        _confirmedDisk = picker.ChosenDisk;

        try
        {
            AgentRun run = await server.PickSequenceAsync(machineId, token, request, cancellationToken).ConfigureAwait(false);
            _pickedWithoutErase = request.DiskNumber is null ? run.Id : null;
            picker.Picked(run);
        }
        catch (Exception exception) when (ServerCallRules.IsRefusal(exception))
        {
            picker.Refused(ServerCallRules.Reason(exception, "the choice"));
        }
        catch (Exception exception) when (IsTransient(exception) && !cancellationToken.IsCancellationRequested)
        {
            picker.NotSent(exception);
        }
    }

    // Asks for the sequences until there are some, and reads the disks once per stretch in which the machine may pick,
    // when a sequence erases one. With no disk only sequences that erase none can be offered: a restart is the only
    // way a disk appears.
    private async Task OfferSequencesAsync(SequencePicker picker, Guid machineId, string token, CancellationToken cancellationToken)
    {
        IReadOnlyList<AgentSequenceChoice> sequences;

        try
        {
            sequences = await server.GetSequencesAsync(machineId, token, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (ServerCallRules.IsRefusal(exception))
        {
            // The machine may no longer pick; the next poll says so.
            return;
        }

        if (sequences.Count == 0)
        {
            if (!_toldNoSequences)
            {
                _toldNoSequences = true;
                log.Warning("The server has no task sequence this machine can run. Create one on the Sequences page.");
            }

            return;
        }

        _toldNoSequences = false;

        if (_pickableDisks is null && sequences.Any(sequence => sequence.ErasesDisk))
        {
            _pickableDisks = await disks.ListDisksAsync(cancellationToken).ConfigureAwait(false);

            if (_pickableDisks.Count == 0)
            {
                log.Error(SequenceRunner.NoDiskMessage);
            }
        }

        picker.Offer(sequences, _pickableDisks ?? []);
    }

    // Waits for the prompt to let go of the console before anything else can ask for input.
    private static async Task StopTypingAsync(CancellationTokenSource stopTyping, Task<string?> typing)
    {
        await stopTyping.CancelAsync().ConfigureAwait(false);
        await typing.ConfigureAwait(false);
        stopTyping.Dispose();
    }

    // A log that cannot be delivered must not stop the machine from polling, so a failure other than a
    // refused token leaves the lines queued for the next attempt and says nothing: a warning per failed
    // flush would itself fill the queue.
    private async Task FlushAsync(Guid machineId, string token, CancellationToken cancellationToken)
    {
        try
        {
            await log.FlushAsync(server, machineId, token, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransient(exception) && !cancellationToken.IsCancellationRequested)
        {
        }
    }

    // Looked for before every registration, so a run that a refused token interrupted is found again too.
    private async Task FindLocalRunAsync(CancellationToken cancellationToken)
    {
        Guid? known = _localRun?.State.RunId;
        _localRun = locator.Find() is { } root ? await LocalRun.LoadAsync(root, log, cancellationToken).ConfigureAwait(false) : null;

        if (_localRun is { } local)
        {
            _runToken = local.RunToken ?? _runToken;

            if (local.State.RunId != known)
            {
                log.Information($"Found run {local.State.RunId} on {local.WindowsRoot}. It goes on if the server still runs it.");
            }
        }
    }

    // The server resumes a run only for the run token of its active run, and says so with the run's id.
    private void KeepOrDiscardLocalRun(AgentRegistrationResult registration)
    {
        if (_localRun is { } local && registration.RunId != local.State.RunId)
        {
            local.Discard(log);
            log.Information($"The server ended run {local.State.RunId}; its state on disk was removed.");
            _localRun = null;
        }

        _runToken = registration.RunId is null ? null : registration.RunToken ?? _runToken;
    }

    private async Task<AgentRegistrationResult?> RegisterAsync(CancellationToken cancellationToken)
    {
        int failures = 0;
        IReadOnlyList<AgentDisk>? eligibleDisks = await ReadDisksAsync(cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Read again on every attempt: the adapter with the default route, which is the primary MAC,
                // may only be known once DHCP has finished.
                MachineIdentity identity = identityReader.Read();
                ReportIdentity(identity);

                return await server.RegisterAsync(
                    new AgentRegistration(
                        identity.SmbiosUuid,
                        identity.PrimaryMac,
                        identity.MacAddresses,
                        identity.Manufacturer,
                        identity.Model,
                        identity.SerialNumber,
                        agentVersion,
                        _resumeToken,
                        eligibleDisks,
                        _runToken,
                        SequenceDefinition.CurrentVersion),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (AgentTokenRejectedException)
            {
                failures++;
                log.Warning("The server refused the registration. Trying again.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                failures++;
                log.Warning($"Cannot register with the server ({exception.Message}).");
            }

            if (!await DelayAsync(AgentLimits.RetryDelay(failures), cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
        }

        return null;
    }

    // The server refuses a web assignment to a machine with several disks, so it has to know them. A machine whose
    // disks cannot be read registers without them rather than not at all.
    private async Task<IReadOnlyList<AgentDisk>?> ReadDisksAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<LocalDisk> eligible = await disks.ListDisksAsync(cancellationToken).ConfigureAwait(false);

            return [.. eligible.Select(disk => disk.ToAgentDisk())];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            log.Warning($"The disks cannot be read ({exception.Message}). Registering without them.");

            return null;
        }
    }

    // Kept, as the run's conditions test it.
    private void ReportIdentity(MachineIdentity identity)
    {
        bool known = _lastIdentity is not null
            && _lastIdentity.SmbiosUuid == identity.SmbiosUuid
            && _lastIdentity.PrimaryMac == identity.PrimaryMac;

        _lastIdentity = identity;

        if (known)
        {
            return;
        }

        log.Information($"SMBIOS UUID {identity.SmbiosUuid}, primary MAC {identity.PrimaryMac}");
        log.Information($"{identity.Manufacturer} {identity.Model}, serial {identity.SerialNumber}");
    }

    // A Failed report for a run this agent does not run, which names no step.
    private static AgentRunReport FailedBeforeItRan(string error) =>
        new(DeploymentState.Failed, SequencePhase.WindowsPE, [], null, 0, RunActivity.Preparing, error);

    // JsonException covers an HTML page from a wrong URL and a newer server reporting a state this agent
    // does not know; neither may end the agent.
    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or TimeoutException or TaskCanceledException or JsonException;

    private async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static string Describe(MachineState state) => state switch
    {
        MachineState.Pending => "waiting to be authorized",
        MachineState.Approved => "approved, waiting for a task sequence",
        _ => state.ToString(),
    };
}
