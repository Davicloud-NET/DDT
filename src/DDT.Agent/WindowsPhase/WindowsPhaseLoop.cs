// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Agent.WindowsPhase;

// The agent as the DdtSequence service in the installed Windows at windowsRoot, which goes on with the run the
// hand-over left in its DDT directory. It registers with the run's token, and goes on only while the server still runs
// that run; any other answer means the run is over there, and the agent removes itself. Before the next step it waits
// for Windows setup to finish, however long someone takes at the out-of-box experience, and then deletes the answer
// file, which holds passwords. The run ends with its Done report or its failure, after which the agent removes itself
// too, and after the Done report Windows restarts once more to delete the agent's last files; or the run goes on after
// a restart of Windows, after which the service starts again. The run records that restart before anything else, so a
// service that starts again without it restarts Windows instead of going on. The report of how the run ended stays on
// the disk with the run token until the server has it, so a stop, or a server out of reach, only puts it off.
//
// With a session, the machine shows the run on DDT's console in a session Windows signs in to by itself. The session
// is prepared at every start, before anything that can take long, and ends before the agent removes itself: at once
// when the run is done or over on the server, and after a failure only once someone at the machine has read it and
// signed out. status is what the console shows.
public sealed class WindowsPhaseLoop(
    IAgentServer server,
    IMachineIdentityReader identityReader,
    SequenceRunner runner,
    IWindowsSetupProbe setup,
    IRebooter rebooter,
    IRestartMarker restartMarker,
    IAgentRemoval removal,
    AgentLog log,
    TimeProvider timeProvider,
    TimeSpan heartbeatInterval,
    string windowsRoot,
    string agentVersion,
    bool dryRun,
    IDeploySession? session = null,
    ConsoleStatus? status = null)
{
    public const string StateGoneMessage = "The run's state in the installed Windows is gone, so the run cannot go on there.";

    public const string FinalReportLostMessage = "The run ended in the installed Windows, but how it ended cannot be read from the disk there.";

    public static readonly TimeSpan SetupPollInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SetupWarningInterval = TimeSpan.FromMinutes(30);

    // How long a restart may take to stop the service before it is asked for again.
    public static readonly TimeSpan RestartTimeout = TimeSpan.FromMinutes(5);

    private MachineIdentity? _identity;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GoOnAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            log.Information("The agent was stopped. The run goes on when the service starts again.");

            return AgentExitCodes.Stopped;
        }
    }

    private async Task<int> GoOnAsync(CancellationToken cancellationToken)
    {
        log.Information($"DDT agent {agentVersion}, the {OfflineServiceRegistration.ServiceName} service of the installed Windows.");

        LocalRun? local = await LocalRun.LoadAsync(windowsRoot, log, cancellationToken).ConfigureAwait(false);

        if (local is null)
        {
            log.Warning($"There is no run to go on with in {RunFiles.StatePathIn(windowsRoot)}.");

            return await RemoveAsync(AgentExitCodes.Stopped, signOut: true, cancellationToken).ConfigureAwait(false);
        }

        Guid runId = local.State.RunId;

        // The hand-over saves the Windows phase only once this service is registered and before Windows can start, so
        // Windows PE hands the run over again.
        if (local.State.Phase != SequencePhase.Windows)
        {
            log.Warning($"Run {runId} still goes on in Windows PE, which hands it over again. The agent leaves it alone.");

            return AgentExitCodes.Stopped;
        }

        // Before the registration, which waits for the network: Windows may sign in any moment after it starts.
        if (session is not null)
        {
            await session.PrepareAsync(cancellationToken).ConfigureAwait(false);
        }

        string? runToken = local.RunToken;
        AgentRunReport? unsent = null;

        for (int attempt = 0; ; attempt++)
        {
            // The state already says the step before the restart is done, so nothing may go on before the restart:
            // neither a service the agent's stop left without it, nor this loop when a run comes back without it.
            if (restartMarker.IsSet)
            {
                log.Warning("Windows was to restart for the run but has not restarted since. Restarting it now.");
                await RestartAsync(cancellationToken).ConfigureAwait(false);

                return await WaitForRestartAsync(cancellationToken).ConfigureAwait(false);
            }

            if (attempt > 0)
            {
                await Task.Delay(AgentLimits.RetryDelay(attempt), timeProvider, cancellationToken).ConfigureAwait(false);
            }

            AgentRegistrationResult? registration = await RegisterAsync(runToken, cancellationToken).ConfigureAwait(false);

            if (registration is not { Token: { } token, ResumeToken: { } resumeToken } || registration.RunId != runId)
            {
                return await RunIsOverAsync(runId, cancellationToken).ConfigureAwait(false);
            }

            Guid machineId = registration.MachineId;
            status?.Registered(machineId);
            runToken = registration.RunToken ?? runToken;
            DeploymentTokens tokens = new(token, resumeToken, runToken);
            AgentRun? run;

            try
            {
                AgentNextResult next = await server.NextAsync(machineId, tokens.Token, cancellationToken).ConfigureAwait(false);
                tokens.Update(next.Token, next.ResumeToken);
                run = next.Run;
            }
            catch (Exception exception) when (exception is AgentTokenRejectedException || ServerCallRules.IsTransient(exception, cancellationToken))
            {
                log.Warning($"Cannot ask the server about the run ({exception.Message}).");

                continue;
            }

            if (run is not { State: DeploymentState.Running } || run.Id != runId)
            {
                return await RunIsOverAsync(runId, cancellationToken).ConfigureAwait(false);
            }

            local = await LocalRun.LoadAsync(windowsRoot, log, cancellationToken).ConfigureAwait(false);

            // All the server can still learn about the run is why it ended here.
            if (local is null || local.State.RunId != runId)
            {
                if (unsent is null)
                {
                    log.Error(StateGoneMessage);
                }

                AgentRunReport lastReport = unsent ?? Failed(StateGoneMessage);

                if (!await ReportAsync(machineId, tokens, runId, lastReport, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                return await EndAsync(lastReport, cancellationToken).ConfigureAwait(false);
            }

            unsent = null;

            // The run is over here, and only the server does not know yet how it ended.
            if (await FinalReportAsync(local, cancellationToken).ConfigureAwait(false) is { } final)
            {
                if (!await ReportAsync(machineId, tokens, runId, final, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                local.Discard(log);

                return await EndAsync(final, cancellationToken).ConfigureAwait(false);
            }

            if (!await WaitForSetupAsync(machineId, run, local, tokens, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            session?.SetupFinished();
            DeleteAnswerFile(local);

            RunResult result = await runner
                .GoOnInWindowsAsync(machineId, run, local, tokens, _identity!, restartMarker.Set, cancellationToken)
                .ConfigureAwait(false);
            runToken = tokens.RunToken ?? runToken;

            switch (result.Outcome)
            {
                case RunOutcome.Finished when result.UnsentReport is null:
                    return await RemoveAndRestartAsync(cancellationToken).ConfigureAwait(false);
                case RunOutcome.Restarting:
                    return await WaitForRestartAsync(cancellationToken).ConfigureAwait(false);
                case RunOutcome.Stopped:
                    return AgentExitCodes.Stopped;
                case RunOutcome.Failed when result.UnsentReport is null:
                    return await RemoveAsync(AgentExitCodes.Stopped, signOut: false, cancellationToken).ConfigureAwait(false);
                default:
                    // A last report the server did not get, kept on the disk with the token, or a refused token: the
                    // next registration decides, and the report goes out while the server still runs the run.
                    unsent = result.UnsentReport;

                    continue;
            }
        }
    }

    // Tries until the server answers, as the network may still be coming up while Windows starts. Null when the server
    // has nothing for this service to go on with.
    private async Task<AgentRegistrationResult?> RegisterAsync(string? runToken, CancellationToken cancellationToken)
    {
        status?.Registering();

        for (int failures = 0; ; failures++)
        {
            if (failures > 0)
            {
                await Task.Delay(AgentLimits.RetryDelay(failures), timeProvider, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                MachineIdentity identity = identityReader.Read();
                _identity = identity;
                status?.Identified(identity);

                return await server.RegisterAsync(
                    new AgentRegistration(
                        identity.SmbiosUuid,
                        identity.PrimaryMac,
                        identity.MacAddresses,
                        identity.Manufacturer,
                        identity.Model,
                        identity.SerialNumber,
                        agentVersion,
                        RunToken: runToken,
                        SequenceVersion: SequenceDefinition.CurrentVersion,
                        Environment: AgentEnvironment.Windows,
                        SecureBootEnabled: identity.SecureBootEnabled,
                        TrustedUefiCas: identity.TrustedUefiCas,
                        ChassisType: identity.ChassisType),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
            {
                log.Information(ServerCallRules.Reason(exception, "the registration"));

                return null;
            }
            catch (Exception exception) when (exception is AgentTokenRejectedException || ServerCallRules.IsTransient(exception, cancellationToken))
            {
                log.Warning($"Cannot register with the server ({exception.Message}).");
                status?.Unreachable(exception);
            }
        }
    }

    // False when the wait ended before setup finished because the server did not take the machine's report, which the
    // next registration sorts out.
    private async Task<bool> WaitForSetupAsync(Guid machineId, AgentRun run, LocalRun local, DeploymentTokens tokens, CancellationToken cancellationToken)
    {
        if (setup.Pending() is not { } pending)
        {
            return true;
        }

        log.Information($"Waiting for Windows setup to finish: {pending}. The run goes on once it has, however long that takes.");
        status?.WaitingForSetup(run, local.State);

        FileRunStateStore store = new(tokens);
        await store.AttachAsync(local.Files, cancellationToken).ConfigureAwait(false);
        RunHeartbeat heartbeat = new(server, log, tokens, machineId, local.State.RunId, store.SaveTokenAsync, heartbeatInterval, timeProvider);
        heartbeat.Update(local.State);
        heartbeat.Activity = RunActivity.WaitingForWindowsSetup;

        using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        heartbeat.Start(waiting, cancellationToken);
        long started = timeProvider.GetTimestamp();
        TimeSpan warnAfter = SetupWarningInterval;

        try
        {
            while (pending is not null)
            {
                await Task.Delay(SetupPollInterval, timeProvider, waiting.Token).ConfigureAwait(false);
                pending = setup.Pending();
                TimeSpan waited = timeProvider.GetElapsedTime(started);

                if (pending is not null && waited >= warnAfter)
                {
                    log.Warning($"Windows setup has not finished after {LogText.Duration(waited)}: {pending}. If it waits for someone at the machine, finish it there.");
                    warnAfter += SetupWarningInterval;
                }
            }
        }
        catch (OperationCanceledException) when (waiting.IsCancellationRequested)
        {
        }
        finally
        {
            await heartbeat.StopAsync().ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (heartbeat.Failure is { } failure)
        {
            log.Warning($"The server did not take the report while Windows setup runs ({LogText.OneLine(failure)}). Registering again.");

            return false;
        }

        log.Information("Windows setup has finished.");

        return true;
    }

    // Setup has read the answer file, and its SetupComplete.cmd line may never have run: an OEM key skips it.
    private void DeleteAnswerFile(LocalRun local)
    {
        string path = UnattendFile.PathIn(windowsRoot);
        bool there = File.Exists(path);
        LocalRun.DeleteAnswerFile(local.State, windowsRoot, log);

        if (there && !File.Exists(path))
        {
            log.Information($"Deleted the answer file {path}, which holds passwords.");
        }
    }

    // The restart stops the service, and until then there is nothing to do but ask again now and then, in case the
    // restart never came. A dry run has no service to stop.
    private async Task<int> WaitForRestartAsync(CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            return AgentExitCodes.Restarting;
        }

        try
        {
            while (true)
            {
                await Task.Delay(RestartTimeout, timeProvider, cancellationToken).ConfigureAwait(false);
                log.Warning($"Windows has not restarted {RestartTimeout.TotalMinutes:0} minutes after the run asked it to. Asking again.");
                await RestartAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AgentExitCodes.Restarting;
        }
    }

    // The agent and the log it holds open go only when Windows next starts, and a finished run leaves nothing of DDT
    // behind. Should the service start once more after it, it finds no run and only removes itself, so this restart
    // never leads to another.
    private async Task<int> RemoveAndRestartAsync(CancellationToken cancellationToken)
    {
        if (session is not null && !await session.EndAsync(signOut: true, cancellationToken).ConfigureAwait(false))
        {
            log.Information("The agent was stopped while DDT's session ended. It ends it and removes itself when it next starts.");

            return AgentExitCodes.Deployed;
        }

        await removal.RemoveAsync(CancellationToken.None).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            log.Information("The agent was stopped. What is left of it goes when Windows next starts.");

            return AgentExitCodes.Deployed;
        }

        log.Information("Windows restarts once more, which deletes what is left of the agent.");

        try
        {
            await rebooter.RebootAsync(RestartInto.Windows, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            log.Warning($"Windows could not restart itself ({LogText.OneLine(exception)}). What is left of the agent goes when Windows next starts.");
        }

        return AgentExitCodes.Deployed;
    }

    private async Task RestartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await rebooter.RebootAsync(RestartInto.Windows, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log.Error($"Windows could not restart itself ({LogText.OneLine(exception)}). Restart it by hand; the run goes on after the restart.");
        }
    }

    // True once the server has the report, or refused it.
    private async Task<bool> ReportAsync(Guid machineId, DeploymentTokens tokens, Guid runId, AgentRunReport report, CancellationToken cancellationToken)
    {
        try
        {
            await server.ReportRunAsync(machineId, tokens.Token, runId, report, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (ServerCallRules.IsRefusal(exception))
        {
            log.Warning(ServerCallRules.Reason(exception, "the run's last report"));

            return true;
        }
        catch (Exception exception) when (exception is AgentTokenRejectedException || ServerCallRules.IsTransient(exception, cancellationToken))
        {
            log.Warning($"The run's last report could not be sent ({exception.Message}). Trying again.");

            return false;
        }
    }

    // The report the runner kept once the run was over here, until the server has it; null while the run goes on. One
    // that cannot be read still ends the run: its steps must not run again.
    private async Task<AgentRunReport?> FinalReportAsync(LocalRun local, CancellationToken cancellationToken)
    {
        if (!File.Exists(local.Files.FinalReportPath))
        {
            return null;
        }

        return await local.Files.LoadFinalReportAsync(cancellationToken).ConfigureAwait(false) ?? Failed(FinalReportLostMessage);
    }

    // After the server has the run's last report: Windows restarts once more after a finished run.
    private Task<int> EndAsync(AgentRunReport report, CancellationToken cancellationToken) =>
        report.State == DeploymentState.Done
            ? RemoveAndRestartAsync(cancellationToken)
            : RemoveAsync(AgentExitCodes.Stopped, signOut: false, cancellationToken);

    // The run's state and answer file go first, token first, whatever is still there.
    private async Task<int> RunIsOverAsync(Guid runId, CancellationToken cancellationToken)
    {
        log.Information($"The server no longer runs run {runId} on this machine.");

        if (await LocalRun.LoadAsync(windowsRoot, log, cancellationToken).ConfigureAwait(false) is { } local)
        {
            local.Discard(log);
        }

        return await RemoveAsync(AgentExitCodes.Stopped, signOut: true, cancellationToken).ConfigureAwait(false);
    }

    // The session ends first, which after a failure waits for someone to sign out, and a stop may end that wait: the
    // next start finds the run over and ends what is left. Nothing may stop the removal half way.
    private async Task<int> RemoveAsync(int exitCode, bool signOut, CancellationToken cancellationToken)
    {
        if (session is not null && !await session.EndAsync(signOut, cancellationToken).ConfigureAwait(false))
        {
            log.Information("The agent was stopped while DDT's session ended. It ends it and removes itself when it next starts.");

            return AgentExitCodes.Stopped;
        }

        await removal.RemoveAsync(CancellationToken.None).ConfigureAwait(false);

        return exitCode;
    }

    private static AgentRunReport Failed(string error) =>
        new(DeploymentState.Failed, SequencePhase.Windows, [], null, 0, RunActivity.Preparing, error);
}
