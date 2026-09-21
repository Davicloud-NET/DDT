// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;

namespace DDT.Agent;

public sealed class AgentLoop(
    IAgentServer server,
    IMachineIdentityReader identityReader,
    ISignInPrompt prompt,
    IDiskPartitioner disks,
    DeploymentRunner runner,
    AgentLog log,
    TimeProvider timeProvider,
    string agentVersion)
{
    private const string LostContactMessage = "The agent lost contact with the server during the deployment.";

    private MachineIdentity? _lastIdentity;
    private string? _resumeToken;

    // Where a run ended that the server still has as running, and why, for the report that it failed.
    private (Guid DeploymentId, DeploymentStep Step, int Percent, string Error)? _abandonedRun;

    // The disk last confirmed with ERASE in this process. Kept past the pick's answer: a pick the server stored but
    // did not confirm still arrives as an Assigned deployment.
    private LocalDisk? _confirmedDisk;

    // The disks the picker offers, read once each time the machine may pick; null until then.
    private IReadOnlyList<LocalDisk>? _pickableDisks;
    private bool _toldNoImages;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        log.Information($"DDT agent {agentVersion}");

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

            AgentRegistrationResult? registration = await RegisterAsync(cancellationToken).ConfigureAwait(false);

            if (registration is null)
            {
                return AgentExitCodes.Stopped;
            }

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
    // keyboard runs alongside polling, and a deployment is awaited here, with its own heartbeat instead of polls.
    private async Task<int?> PollAsync(AgentRegistrationResult registration, CancellationToken cancellationToken)
    {
        Guid machineId = registration.MachineId;
        string token = registration.Token!;
        MachineState state = registration.State;
        string? signedInBy = registration.SignedInBy;
        TimeSpan interval = TimeSpan.FromSeconds(registration.PollAfterSeconds);
        int failures = 0;

        SignInConversation conversation = new(prompt, log);
        ImagePicker picker = new(prompt, log);
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

                    AgentDeployment? deployment = state is MachineState.Approved or MachineState.Deploying or MachineState.Failed
                        ? next.Deployment
                        : null;

                    if (deployment is { State: DeploymentState.Assigned or DeploymentState.Running } && typing is not null)
                    {
                        await StopTypingAsync(stopTyping!, typing).ConfigureAwait(false);
                        typing = null;
                    }

                    if (deployment is { State: DeploymentState.Assigned })
                    {
                        DeploymentRunResult result = await runner.RunAsync(machineId, deployment, _confirmedDisk, token, _resumeToken, cancellationToken)
                            .ConfigureAwait(false);

                        _abandonedRun = result.UnsentError is not null || result.Outcome == DeploymentOutcome.TokenRejected
                            ? (deployment.Id, result.Step, result.Percent, result.UnsentError ?? LostContactMessage)
                            : null;

                        switch (result.Outcome)
                        {
                            case DeploymentOutcome.Deployed:
                                return AgentExitCodes.Deployed;
                            case DeploymentOutcome.Stopped:
                                return AgentExitCodes.Stopped;
                            case DeploymentOutcome.TokenRejected:
                                _resumeToken = result.ResumeToken;

                                return null;
                            default:
                                // The runner kept the session alive; the tokens this loop last saw may have expired.
                                token = result.Token;
                                _resumeToken = result.ResumeToken;
                                picker.Reset();
                                _pickableDisks = null;

                                continue;
                        }
                    }

                    // Nothing in this process runs it: the agent restarted, a refused token ended the run, or the run's
                    // own failure report did not get through.
                    if (deployment is { State: DeploymentState.Running })
                    {
                        (DeploymentStep step, int percent, string error) = _abandonedRun is { } run && run.DeploymentId == deployment.Id
                            ? (run.Step, run.Percent, run.Error)
                            : (DeploymentStep.Partition, 0, LostContactMessage);

                        log.Error(error);
                        AgentDeploymentReportResult reported = await server.ReportDeploymentAsync(
                            machineId,
                            token,
                            deployment.Id,
                            new AgentDeploymentReport(DeploymentState.Failed, step, percent, error),
                            cancellationToken).ConfigureAwait(false);

                        token = reported.Token;
                        _resumeToken = reported.ResumeToken;
                        _abandonedRun = null;
                    }

                    bool signInWanted = state == MachineState.Pending && signedInBy is null && conversation.IsAvailable;
                    bool pickWanted = next.CanPickImage && deployment is null && picker.IsAvailable;

                    if (!pickWanted)
                    {
                        picker.Reset();
                        _pickableDisks = null;
                    }
                    else if (!picker.IsOffered)
                    {
                        await OfferImagesAsync(picker, machineId, token, next, cancellationToken).ConfigureAwait(false);
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
                    if (state is MachineState.Approved or MachineState.Deploying or MachineState.Failed)
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
    private async Task SendPickAsync(ImagePicker picker, Guid machineId, string token, string typed, CancellationToken cancellationToken)
    {
        if (picker.Accept(typed) is not { } request)
        {
            return;
        }

        _confirmedDisk = picker.ChosenDisk;

        try
        {
            AgentDeployment deployment = await server.PickImageAsync(machineId, token, request, cancellationToken).ConfigureAwait(false);
            picker.Picked(deployment);
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

    // Reads the disks once per stretch in which the machine may pick, and asks for the images until there are
    // some. With no disk there is nothing to offer: a restart is the only way a disk appears.
    private async Task OfferImagesAsync(ImagePicker picker, Guid machineId, string token, AgentNextResult next, CancellationToken cancellationToken)
    {
        if (_pickableDisks is null)
        {
            _pickableDisks = await disks.ListDisksAsync(cancellationToken).ConfigureAwait(false);

            if (_pickableDisks.Count == 0)
            {
                log.Error(DeploymentRunner.NoDiskMessage);
            }
        }

        if (_pickableDisks.Count == 0)
        {
            return;
        }

        IReadOnlyList<AgentImageChoice> images;

        try
        {
            images = await server.GetImagesAsync(machineId, token, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (ServerCallRules.IsRefusal(exception))
        {
            // The machine may no longer pick; the next poll says so.
            return;
        }

        if (images.Count == 0)
        {
            if (!_toldNoImages)
            {
                _toldNoImages = true;
                log.Warning("The server has no image this machine can install. Upload an x64 Windows image on the Images page.");
            }

            return;
        }

        _toldNoImages = false;
        picker.Offer(images, _pickableDisks, next.DomainConfigured && string.IsNullOrEmpty(next.AssignedName));
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
                        eligibleDisks),
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

    private void ReportIdentity(MachineIdentity identity)
    {
        if (_lastIdentity is not null
            && _lastIdentity.SmbiosUuid == identity.SmbiosUuid
            && _lastIdentity.PrimaryMac == identity.PrimaryMac)
        {
            return;
        }

        _lastIdentity = identity;
        log.Information($"SMBIOS UUID {identity.SmbiosUuid}, primary MAC {identity.PrimaryMac}");
        log.Information($"{identity.Manufacturer} {identity.Model}, serial {identity.SerialNumber}");
    }

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
        MachineState.Approved => "approved, waiting for an image",
        _ => state.ToString(),
    };
}
