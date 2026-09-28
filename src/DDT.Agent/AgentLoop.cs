// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;

namespace DDT.Agent;

// The agent in Windows PE: registers the machine, polls until it may run a task sequence, and runs it. After a restart
// it goes on with the run it finds on the disk, as long as the server still runs it.
public sealed class AgentLoop
{
    private readonly IAgentServer _server;
    private readonly AgentMachine _machine;
    private readonly ConsoleStatus _status;
    private readonly SequenceRunner _runner;
    private readonly AgentLog _log;
    private readonly TimeProvider _timeProvider;
    private readonly AgentRegistrar _registrar;
    private readonly LocalRunTracker _runs;
    private readonly ConsolePrompts _prompts;
    private readonly RunStarter _runStarter;

    // The tokens every registration, poll, report and run renews; the next registration sends their resume token.
    private DeploymentTokens? _tokens;

    private bool _toldNoDeployments;

    public AgentLoop(IAgentServer server, AgentMachine machine, ConsoleStatus status, SequenceRunner runner, AgentLog log, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(machine);

        _server = server;
        _machine = machine;
        _status = status;
        _runner = runner;
        _log = log;
        _timeProvider = timeProvider;
        _registrar = new AgentRegistrar(server, machine, status, log, timeProvider);
        _runs = new LocalRunTracker(machine.Runs, log);
        _prompts = new ConsolePrompts(server, status, machine.Disks, log, _registrar);
        _runStarter = new RunStarter(server, runner, _runs, _prompts, _registrar, log);
    }

    // Shows the logo the registration names on the graphical console; null for the text console.
    public ConsoleLogo? Logo { get; init; }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        int exitCode = await RunCoreAsync(cancellationToken).ConfigureAwait(false);

        if (exitCode == AgentExitCodes.Stopped && _status.State.Stage != ConsoleStage.Stopped)
        {
            _status.Stopped("The agent was stopped.");
        }

        return exitCode;
    }

    private async Task<int> RunCoreAsync(CancellationToken cancellationToken)
    {
        _log.Information($"DDT agent {_machine.AgentVersion}");

        // A start that finds a restart still due makes it first, without registering.
        if (await _runner.RestartIfDueAsync(cancellationToken).ConfigureAwait(false) is { } restarted)
        {
            return restarted == RunOutcome.Restarting ? AgentExitCodes.Restarting : AgentExitCodes.Stopped;
        }

        if (!_status.Console.CanAsk)
        {
            _log.Information("Nobody can type at this console. Unless the server requires a sign in at the machine, approve it on the Machines page.");
        }

        bool first = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            // After a refused token, never register again in a tight loop: two agents fighting over one
            // machine would otherwise hammer the server.
            if (!first && !await CancellableDelay.WaitAsync(AgentLimits.MinRetryDelay, _timeProvider, cancellationToken).ConfigureAwait(false))
            {
                return AgentExitCodes.Stopped;
            }

            first = false;

            if (await RegisterAsync(cancellationToken).ConfigureAwait(false) is not { } registration)
            {
                return AgentExitCodes.Stopped;
            }

            if (registration.Token is not { } token)
            {
                _log.Error($"An administrator rejected machine {registration.MachineId}. The agent stops here.");
                _status.Rejected(registration.MachineId);

                return AgentExitCodes.Rejected;
            }

            // The server sends a resume token with every token; one missing and never renewed means the next
            // registration sends none.
            _tokens = new DeploymentTokens(token, registration.ResumeToken ?? string.Empty);
            _log.Information($"Registered as machine {registration.MachineId}, {Describe(registration.State)}");
            _status.Reached(registration.MachineId, registration.State, registration.SignedInBy);

            if (await PollAsync(new PollState(registration, _tokens), cancellationToken).ConfigureAwait(false) is { } exitCode)
            {
                return exitCode;
            }
        }

        return AgentExitCodes.Stopped;
    }

    // Null once stopped. The console takes up the language and the logo the registration names.
    private async Task<AgentRegistrationResult?> RegisterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _runs.FindAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        string? resumeToken = _tokens?.ResumeToken is { Length: > 0 } resume ? resume : null;
        AgentRegistrationResult? registration = await _registrar.RegisterAsync(resumeToken, _runs.RunToken, cancellationToken).ConfigureAwait(false);

        if (registration is null)
        {
            return null;
        }

        _runs.KeepOrDiscard(registration);
        _status.SetLanguage(registration.ConsoleLanguage);

        if (Logo is not null)
        {
            await Logo.ShowAsync(registration.ConsoleLogoSha256, cancellationToken).ConfigureAwait(false);
        }

        return registration;
    }

    // Returns null to register again, or an exit code. Requests stay on this loop, in order: only reading the
    // keyboard runs alongside polling, and a run is awaited here, with its own heartbeat instead of polls.
    private async Task<int?> PollAsync(PollState poll, CancellationToken cancellationToken)
    {
        _prompts.StartSession();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PollStep polled = await PollStepAsync(poll, cancellationToken).ConfigureAwait(false);

                if (polled.Ends)
                {
                    return polled.ExitCode;
                }

                if (polled.AtOnce)
                {
                    continue;
                }

                (bool stopped, ConsoleAnswer? answer) = await WaitForPollOrAnswerAsync(poll.Interval, cancellationToken).ConfigureAwait(false);

                if (stopped)
                {
                    return AgentExitCodes.Stopped;
                }

                // Poll before asking for the next field, so an approval or an assignment on the web is noticed
                // right away.
                if (answer is not null && await SendAnswerAsync(poll, answer, cancellationToken).ConfigureAwait(false) is { Ends: true } sent)
                {
                    return sent.ExitCode;
                }
            }

            return AgentExitCodes.Stopped;
        }
        finally
        {
            await _prompts.StopTypingAsync().ConfigureAwait(false);
        }
    }

    private async Task<PollStep> PollStepAsync(PollState poll, CancellationToken cancellationToken)
    {
        try
        {
            if (await PollOnceAsync(poll, cancellationToken).ConfigureAwait(false) is not { } outcome)
            {
                return PollStep.Wait;
            }

            // A run that failed leaves the machine to pick again, which it asks the server about at once.
            return outcome == RunOutcome.TokenRejected ? PollStep.End(null)
                : RunStarter.ExitCodeAfter(outcome) is { } exitCode ? PollStep.End(exitCode)
                : PollStep.Now;
        }
        catch (AgentTokenRejectedException)
        {
            _log.Warning("The server no longer accepts this machine's token. Registering again.");

            return PollStep.End(null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PollStep.End(AgentExitCodes.Stopped);
        }
        catch (Exception exception) when (LoopCallRules.IsTransient(exception))
        {
            poll.Failures++;
            poll.Interval = AgentLimits.RetryDelay(poll.Failures);
            _log.Warning($"Cannot reach the server ({exception.Message}). Retrying in {poll.Interval.TotalSeconds:0} s.");
            _status.Unreachable(exception);

            return PollStep.Wait;
        }
    }

    // The outcome of a run this poll ran, or null when it ran none.
    private async Task<RunOutcome?> PollOnceAsync(PollState poll, CancellationToken cancellationToken)
    {
        AgentNextResult next = await _server.NextAsync(poll.MachineId, poll.Tokens.Token, cancellationToken).ConfigureAwait(false);
        AgentRun? run = ApplyNext(poll, next);

        if (run is { State: DeploymentState.Assigned or DeploymentState.Running })
        {
            await _prompts.StopTypingAsync().ConfigureAwait(false);
        }

        if (await _runStarter.HandleAsync(poll.MachineId, poll.Tokens, run, cancellationToken).ConfigureAwait(false) is { } outcome)
        {
            return outcome;
        }

        bool signInWanted = poll.State == MachineState.Pending && poll.SignedInBy is null && _prompts.CanSignIn;
        bool pickWanted = next.CanPickSequence && run is null && _prompts.CanPick;
        bool choosing = await _prompts.UpdateAsync(poll.MachineId, poll.Tokens.Token, signInWanted, pickWanted, cancellationToken).ConfigureAwait(false);

        _status.Reached(poll.MachineId, poll.State, poll.SignedInBy, choosing);

        // Only an authorized machine may write to the server's log. Until then lines wait here.
        if (IsAuthorized(poll.State))
        {
            await FlushAsync(poll, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    // Takes up the server's answer, and returns the run the machine may run, if any.
    private AgentRun? ApplyNext(PollState poll, AgentNextResult next)
    {
        poll.Failures = 0;
        poll.Interval = TimeSpan.FromSeconds(next.PollAfterSeconds);
        poll.Tokens.Update(next.Token, next.ResumeToken);

        if (next.State != poll.State)
        {
            poll.State = next.State;
            _log.Information($"Machine is now {Describe(poll.State)}");
        }

        if (next.SignedInBy != poll.SignedInBy)
        {
            poll.SignedInBy = next.SignedInBy;

            if (poll.State == MachineState.Pending && poll.SignedInBy is not null)
            {
                _log.Information($"{poll.SignedInBy} signed in at this machine. An operator still has to approve it on the Machines page.");
            }
        }

        bool authorized = IsAuthorized(poll.State);

        if (authorized && next.Deployment is not null && next.Run is null && !_toldNoDeployments)
        {
            _toldNoDeployments = true;
            _log.Warning("The server assigned an image deployment, which this agent no longer runs. Assign a task sequence instead.");
        }

        return authorized ? next.Run : null;
    }

    // Stopped once cancelled; otherwise the answer typed before the next poll is due, if any.
    private async Task<(bool Stopped, ConsoleAnswer? Answer)> WaitForPollOrAnswerAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        if (_prompts.Typing is not { } typing)
        {
            return (!await CancellableDelay.WaitAsync(interval, _timeProvider, cancellationToken).ConfigureAwait(false), null);
        }

        using (CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            Task elapsed = Task.Delay(interval, _timeProvider, waiting.Token);

            if (await Task.WhenAny(typing, elapsed).ConfigureAwait(false) != typing)
            {
                return (cancellationToken.IsCancellationRequested, null);
            }

            await waiting.CancelAsync().ConfigureAwait(false);
        }

        return (false, await _prompts.TakeAnswerAsync().ConfigureAwait(false));
    }

    private async Task<PollStep> SendAnswerAsync(PollState poll, ConsoleAnswer answer, CancellationToken cancellationToken)
    {
        try
        {
            await _prompts.SendAsync(poll.MachineId, poll.Tokens.Token, answer, cancellationToken).ConfigureAwait(false);

            return PollStep.Wait;
        }
        catch (AgentTokenRejectedException)
        {
            _log.Warning("The server no longer accepts this machine's token. Registering again.");

            return PollStep.End(null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PollStep.End(AgentExitCodes.Stopped);
        }
    }

    // A failure other than a refused token leaves the lines queued for the next attempt, silently: a warning per
    // failed flush would itself fill the queue.
    private async Task FlushAsync(PollState poll, CancellationToken cancellationToken)
    {
        try
        {
            await _log.FlushAsync(_server, poll.MachineId, poll.Tokens.Token, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (LoopCallRules.IsTransient(exception) && !cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static bool IsAuthorized(MachineState state) => state is MachineState.Approved or MachineState.Deploying or MachineState.Failed;

    private static string Describe(MachineState state) => state switch
    {
        MachineState.Pending => "waiting to be authorized",
        MachineState.Approved => "approved, waiting for a task sequence",
        _ => state.ToString(),
    };

    // One registration's polling: the machine's tokens, its state and sign-in as the server last said, and when to
    // poll next.
    private sealed class PollState(AgentRegistrationResult registration, DeploymentTokens tokens)
    {
        public Guid MachineId { get; } = registration.MachineId;

        public DeploymentTokens Tokens { get; } = tokens;

        public MachineState State { get; set; } = registration.State;

        public string? SignedInBy { get; set; } = registration.SignedInBy;

        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(registration.PollAfterSeconds);

        public int Failures { get; set; }
    }

    // What polling does next: waits for the next poll, polls again at once, or ends with ExitCode, null to register again.
    private readonly record struct PollStep(bool Ends, int? ExitCode, bool AtOnce)
    {
        public static PollStep Wait => new(false, null, false);

        public static PollStep Now => new(false, null, true);

        public static PollStep End(int? exitCode) => new(true, exitCode, false);
    }
}
