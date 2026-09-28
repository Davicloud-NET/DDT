// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Agent.WindowsPhase;

// The DdtSequence service in the installed Windows: goes on with the run the hand-over left in its DDT directory while
// the server still runs it, and removes the agent once the run is over. How the run ended stays on the disk with the
// run token until the server has it, so a stop, or a server out of reach, only puts the report off.
public sealed class WindowsPhaseLoop
{
    public const string StateGoneMessage = "The run's state in the installed Windows is gone, so the run cannot go on there.";

    public const string FinalReportLostMessage = "The run ended in the installed Windows, but how it ended cannot be read from the disk there.";

    public static readonly TimeSpan SetupPollInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SetupWarningInterval = TimeSpan.FromMinutes(30);

    // How long a restart may take to stop the service before it is asked for again.
    public static readonly TimeSpan RestartTimeout = TimeSpan.FromMinutes(5);

    private readonly WindowsRunCalls _calls;
    private readonly SequenceRunner _runner;
    private readonly WindowsSetupWait _setup;
    private readonly AgentExit _exit;
    private readonly WindowsPhaseOptions _options;
    private readonly AgentLog _log;

    // Internal, as its parts are: WindowsPhaseLoopBuilder puts it together.
    internal WindowsPhaseLoop(
        WindowsRunCalls calls,
        SequenceRunner runner,
        WindowsSetupWait setup,
        AgentExit exit,
        WindowsPhaseOptions options,
        AgentLog log)
    {
        _calls = calls;
        _runner = runner;
        _setup = setup;
        _exit = exit;
        _options = options;
        _log = log;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GoOnAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _log.Information("The agent was stopped. The run goes on when the service starts again.");

            return AgentExitCodes.Stopped;
        }
    }

    private async Task<int> GoOnAsync(CancellationToken cancellationToken)
    {
        _log.Information($"DDT agent {_options.AgentVersion}, the {OfflineServiceRegistration.ServiceName} service of the installed Windows.");

        if (await LocalRun.LoadAsync(_options.WindowsRoot, _log, cancellationToken).ConfigureAwait(false) is not { } local)
        {
            _log.Warning($"There is no run to go on with in {RunFiles.StatePathIn(_options.WindowsRoot)}.");

            return await _exit.RemoveAsync(AgentExitCodes.Stopped, signOut: true, cancellationToken).ConfigureAwait(false);
        }

        // The hand-over saves the Windows phase only once this service is registered and before Windows can start, so
        // Windows PE hands the run over again.
        if (local.State.Phase != SequencePhase.Windows)
        {
            _log.Warning($"Run {local.State.RunId} still goes on in Windows PE, which hands it over again. The agent leaves it alone.");

            return AgentExitCodes.Stopped;
        }

        await _setup.PrepareSessionAsync(cancellationToken).ConfigureAwait(false);

        return await GoOnWithRunAsync(local.State.RunId, local.RunToken, cancellationToken).ConfigureAwait(false);
    }

    // Registers with the run token until the server answers, and goes on only while it still runs that run.
    private async Task<int> GoOnWithRunAsync(Guid runId, string? runToken, CancellationToken cancellationToken)
    {
        NextAttempt next = new() { RunToken = runToken };

        for (int attempt = 0; ; attempt++)
        {
            if (await _exit.RestartIfDueAsync(cancellationToken).ConfigureAwait(false) is { } restarting)
            {
                return restarting;
            }

            if (await AttemptAsync(runId, attempt, next, cancellationToken).ConfigureAwait(false) is { } exitCode)
            {
                return exitCode;
            }
        }
    }

    // One registration and what follows it. Null when the next registration decides.
    private async Task<int?> AttemptAsync(Guid runId, int attempt, NextAttempt next, CancellationToken cancellationToken)
    {
        if (await _calls.RegisterAsync(next.RunToken, runId, attempt, cancellationToken).ConfigureAwait(false) is not { } contact)
        {
            return await RunIsOverAsync(runId, cancellationToken).ConfigureAwait(false);
        }

        next.RunToken = contact.Tokens.RunToken;

        if (await _calls.AskAsync(contact, cancellationToken).ConfigureAwait(false) is not { } answer)
        {
            return null;
        }

        if (answer.Run is not { State: DeploymentState.Running } run || run.Id != runId)
        {
            return await RunIsOverAsync(runId, cancellationToken).ConfigureAwait(false);
        }

        LocalRun? local = await LocalRun.LoadAsync(_options.WindowsRoot, _log, cancellationToken).ConfigureAwait(false);

        if (local is null || local.State.RunId != runId)
        {
            return await ReportLostStateAsync(contact, runId, next.Unsent, cancellationToken).ConfigureAwait(false);
        }

        next.Unsent = null;

        if (await FinalReportAsync(local, cancellationToken).ConfigureAwait(false) is { } final)
        {
            return await SendFinalReportAsync(contact, runId, local, final, cancellationToken).ConfigureAwait(false);
        }

        if (!await _setup.WaitAsync(contact, run, local, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        RunResult result = await _runner
            .GoOnInWindowsAsync(new RunRequest(contact.MachineId, run, local, null, contact.Tokens, contact.Identity), _exit.RecordRestart, cancellationToken)
            .ConfigureAwait(false);
        next.RunToken = contact.Tokens.RunToken ?? next.RunToken;
        next.Unsent = result.UnsentReport;

        return await ExitCodeForAsync(result, cancellationToken).ConfigureAwait(false);
    }

    // All the server can still learn about the run is why it ended here. Null when the report did not get through.
    private async Task<int?> ReportLostStateAsync(WindowsRunContact contact, Guid runId, AgentRunReport? unsent, CancellationToken cancellationToken)
    {
        if (unsent is null)
        {
            _log.Error(StateGoneMessage);
        }

        AgentRunReport lastReport = unsent ?? FailedRunReport.Of(SequencePhase.Windows, StateGoneMessage);

        if (!await _calls.ReportAsync(contact, runId, lastReport, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await _exit.EndAsync(lastReport, cancellationToken).ConfigureAwait(false);
    }

    // The run is over here, and only the server does not know yet how it ended. Null when the report did not get
    // through.
    private async Task<int?> SendFinalReportAsync(
        WindowsRunContact contact,
        Guid runId,
        LocalRun local,
        AgentRunReport final,
        CancellationToken cancellationToken)
    {
        if (!await _calls.ReportAsync(contact, runId, final, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        local.Discard(_log);

        return await _exit.EndAsync(final, cancellationToken).ConfigureAwait(false);
    }

    // Null when the next registration decides: the server did not get the run's last report, kept on the disk with the
    // token, or refused the token.
    private async Task<int?> ExitCodeForAsync(RunResult result, CancellationToken cancellationToken) => result.Outcome switch
    {
        RunOutcome.Finished when result.UnsentReport is null => await _exit.RemoveAndRestartAsync(cancellationToken).ConfigureAwait(false),
        RunOutcome.Restarting => await _exit.WaitForRestartAsync(cancellationToken).ConfigureAwait(false),
        RunOutcome.Stopped => AgentExitCodes.Stopped,
        RunOutcome.Failed when result.UnsentReport is null => await _exit.RemoveAsync(AgentExitCodes.Stopped, signOut: false, cancellationToken).ConfigureAwait(false),
        _ => null,
    };

    // The report the runner kept once the run was over here, until the server has it; null while the run goes on. One
    // that cannot be read still ends the run: its steps must not run again.
    private async Task<AgentRunReport?> FinalReportAsync(LocalRun local, CancellationToken cancellationToken)
    {
        if (!File.Exists(local.Files.FinalReportPath))
        {
            return null;
        }

        return await local.Files.LoadFinalReportAsync(cancellationToken).ConfigureAwait(false)
            ?? FailedRunReport.Of(SequencePhase.Windows, FinalReportLostMessage);
    }

    // The run's state and answer file go first, token first, whatever is still there.
    private async Task<int> RunIsOverAsync(Guid runId, CancellationToken cancellationToken)
    {
        _log.Information($"The server no longer runs run {runId} on this machine.");

        if (await LocalRun.LoadAsync(_options.WindowsRoot, _log, cancellationToken).ConfigureAwait(false) is { } local)
        {
            local.Discard(_log);
        }

        return await _exit.RemoveAsync(AgentExitCodes.Stopped, signOut: true, cancellationToken).ConfigureAwait(false);
    }

    // What an attempt leaves for the next: the newest run token, and a last report the server did not get.
    private sealed class NextAttempt
    {
        public string? RunToken { get; set; }

        public AgentRunReport? Unsent { get; set; }
    }
}
