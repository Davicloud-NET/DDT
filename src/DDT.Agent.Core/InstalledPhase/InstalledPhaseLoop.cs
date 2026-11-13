// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;

namespace DDT.Agent.InstalledPhase;

// The agent's service in the installed system. Goes on with the handed-over run, then removes the agent.
public sealed class InstalledPhaseLoop
{
    public static readonly TimeSpan SetupPollInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SetupWarningInterval = TimeSpan.FromMinutes(30);

    // How long a restart may take to stop the service before it is asked for again.
    public static readonly TimeSpan RestartTimeout = TimeSpan.FromMinutes(5);

    private readonly InstalledPhaseCalls _calls;
    private readonly SequenceRunner _runner;
    private readonly InstalledPhaseSetupWait _setup;
    private readonly InstalledPhaseExit _exit;
    private readonly InstalledPhaseOptions _options;
    private readonly InstalledPhaseProfile _profile;
    private readonly AgentLog _log;
    private InstalledPhaseContact? _contact;

    // Internal, like its parts. InstalledPhaseLoopBuilder puts it together.
    internal InstalledPhaseLoop(
        InstalledPhaseCalls calls,
        SequenceRunner runner,
        InstalledPhaseSetupWait setup,
        InstalledPhaseExit exit,
        InstalledPhaseOptions options,
        AgentLog log)
    {
        _calls = calls;
        _runner = runner;
        _setup = setup;
        _exit = exit;
        _options = options;
        _profile = options.Profile;
        _log = log;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        int exitCode;

        try
        {
            exitCode = await GoOnAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _log.Information("The agent was stopped. The run goes on when the service starts again.");
            exitCode = AgentExitCodes.Stopped;
        }

        // No heartbeat runs any more, and the server would hear of the lines since its last beat only after the restart
        if (cancellationToken.IsCancellationRequested && !_exit.Removed && _contact is { } contact)
        {
            await _calls.SendLastLinesAsync(contact).ConfigureAwait(false);
        }

        return exitCode;
    }

    private async Task<int> GoOnAsync(CancellationToken cancellationToken)
    {
        _log.Information(_profile.Texts.Started(_options.AgentVersion));
        RunFiles.In(_options.Root, _log).Recover();

        if (await LocalRun.LoadAsync(_options.Root, _log, cancellationToken).ConfigureAwait(false) is not { } local)
        {
            _log.Warning($"There is no run to go on with in {RunFiles.StatePathIn(_options.Root)}.");

            return await _exit.RemoveAsync(AgentExitCodes.Stopped, signOut: true, cancellationToken).ConfigureAwait(false);
        }

        // The hand-over only saves the installed phase once this service is registered, before the system can start. So
        // without it, the boot environment hands the run over again.
        if (local.State.Phase != _profile.Phase)
        {
            _log.Warning(_profile.Texts.StillInBootEnvironment(local.State.RunId));

            return AgentExitCodes.Stopped;
        }

        if (_profile.AutomaticRepair is { } repair)
        {
            await repair.TurnOffAsync(cancellationToken).ConfigureAwait(false);
        }

        await _setup.PrepareSessionAsync(cancellationToken).ConfigureAwait(false);

        return await GoOnWithRunAsync(local.State.RunId, local.RunToken, cancellationToken).ConfigureAwait(false);
    }

    // Registers with the run token until the server answers, and only continues while the server still has that run
    // going.
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
        _contact = contact;

        if (await _calls.AskAsync(contact, cancellationToken).ConfigureAwait(false) is not { } answer)
        {
            return null;
        }

        if (answer.Run is not { State: DeploymentState.Running } run || run.Id != runId)
        {
            return await RunIsOverAsync(runId, cancellationToken).ConfigureAwait(false);
        }

        LocalRun? local = await LocalRun.LoadAsync(_options.Root, _log, cancellationToken).ConfigureAwait(false);

        if (local is null || local.State.RunId != runId)
        {
            _exit.RunEnded(run, next.Unsent?.State ?? DeploymentState.Failed);

            return await ReportLostStateAsync(contact, runId, next.Unsent, cancellationToken).ConfigureAwait(false);
        }

        next.Unsent = null;

        if (await FinalReportAsync(local, cancellationToken).ConfigureAwait(false) is { } final)
        {
            _exit.RunEnded(run, final.State);

            return await SendFinalReportAsync(contact, runId, local, final, cancellationToken).ConfigureAwait(false);
        }

        if (!await _setup.WaitAsync(contact, run, local, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        RunResult result = await _runner
            .GoOnInInstalledSystemAsync(
                new RunRequest(contact.MachineId, run, local, null, contact.Tokens, contact.Identity),
                _profile.Phase,
                _exit.RecordRestart,
                cancellationToken)
            .ConfigureAwait(false);
        next.RunToken = contact.Tokens.RunToken ?? next.RunToken;
        next.Unsent = result.UnsentReport;

        if (result.Outcome is RunOutcome.Finished or RunOutcome.Failed)
        {
            _exit.RunEnded(run, result.Outcome == RunOutcome.Finished ? DeploymentState.Done : DeploymentState.Failed);
        }

        return await ExitCodeForAsync(result, cancellationToken).ConfigureAwait(false);
    }

    // All the server can still learn about the run is why it ended here. Returns null when the report didn't get
    // through.
    private async Task<int?> ReportLostStateAsync(InstalledPhaseContact contact, Guid runId, AgentRunReport? unsent, CancellationToken cancellationToken)
    {
        if (unsent is null)
        {
            _log.Error(_profile.Texts.StateGone);
        }

        AgentRunReport lastReport = unsent ?? FailedRunReport.Of(_profile.Phase, _profile.Texts.StateGone);

        if (!await _calls.ReportAsync(contact, runId, lastReport, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await _exit.EndAsync(lastReport, cancellationToken).ConfigureAwait(false);
    }

    // The run is over here, and only the server doesn't know yet how it ended. Returns null when the report didn't get
    // through.
    private async Task<int?> SendFinalReportAsync(
        InstalledPhaseContact contact,
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

    // Returns null when the next registration decides. That's when the server didn't get the run's last report, which
    // is kept on disk with the token, or when it refused the token.
    private async Task<int?> ExitCodeForAsync(RunResult result, CancellationToken cancellationToken) => result.Outcome switch
    {
        RunOutcome.Finished when result.UnsentReport is null => await _exit.RemoveAndRestartAsync(cancellationToken).ConfigureAwait(false),
        RunOutcome.Restarting => await _exit.WaitForRestartAsync(cancellationToken).ConfigureAwait(false),
        RunOutcome.Stopped => AgentExitCodes.Stopped,
        RunOutcome.Failed when result.UnsentReport is null => await _exit.RemoveAsync(AgentExitCodes.Stopped, signOut: false, cancellationToken).ConfigureAwait(false),
        _ => null,
    };

    // The report the runner kept once the run was over here, until the server has it. Null while the run continues.
    // A report that can't be read still ends the run, because its steps must not run again.
    private async Task<AgentRunReport?> FinalReportAsync(LocalRun local, CancellationToken cancellationToken)
    {
        if (!File.Exists(local.Files.FinalReportPath))
        {
            return null;
        }

        return await local.Files.LoadFinalReportAsync(cancellationToken).ConfigureAwait(false)
            ?? FailedRunReport.Of(_profile.Phase, _profile.Texts.FinalReportLost);
    }

    // Deletes the run's state and answer file first, token first, whatever is still there.
    private async Task<int> RunIsOverAsync(Guid runId, CancellationToken cancellationToken)
    {
        _log.Information($"The server no longer runs run {runId} on this machine.");

        if (await LocalRun.LoadAsync(_options.Root, _log, cancellationToken).ConfigureAwait(false) is { } local)
        {
            local.Discard(_log);
        }

        return await _exit.RemoveAsync(AgentExitCodes.Stopped, signOut: true, cancellationToken).ConfigureAwait(false);
    }

    // What an attempt passes to the next: the newest run token, and a last report the server didn't get.
    private sealed class NextAttempt
    {
        public string? RunToken { get; set; }

        public AgentRunReport? Unsent { get; set; }
    }
}
