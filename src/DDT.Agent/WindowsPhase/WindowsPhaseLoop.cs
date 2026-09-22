// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
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
// too, or with a restart of Windows, after which the service starts again and goes on. The run records the restart
// before anything else, so a service that starts again without it restarts Windows instead of going on.
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
    bool dryRun)
{
    public const string StateGoneMessage = "The run's state in the installed Windows is gone, so the run cannot go on there.";

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

            return await RemoveAsync(AgentExitCodes.Stopped).ConfigureAwait(false);
        }

        Guid runId = local.State.RunId;

        // The hand-over saves the Windows phase only once this service is registered and before Windows can start, so
        // Windows PE hands the run over again.
        if (local.State.Phase != SequencePhase.Windows)
        {
            log.Warning($"Run {runId} still goes on in Windows PE, which hands it over again. The agent leaves it alone.");

            return AgentExitCodes.Stopped;
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

                if (!await ReportAsync(machineId, tokens, runId, unsent ?? Failed(StateGoneMessage), cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                return await RemoveAsync(AgentExitCodes.Stopped).ConfigureAwait(false);
            }

            unsent = null;

            if (!await WaitForSetupAsync(machineId, local, tokens, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            DeleteAnswerFile(local);

            RunResult result = await runner
                .GoOnInWindowsAsync(machineId, run, local, tokens, _identity!, restartMarker.Set, cancellationToken)
                .ConfigureAwait(false);
            runToken = tokens.RunToken ?? runToken;

            switch (result.Outcome)
            {
                case RunOutcome.Finished:
                    return await RemoveAsync(AgentExitCodes.Deployed).ConfigureAwait(false);
                case RunOutcome.Restarting:
                    return await WaitForRestartAsync(cancellationToken).ConfigureAwait(false);
                case RunOutcome.Stopped:
                    return AgentExitCodes.Stopped;
                case RunOutcome.Failed when result.UnsentFailure is null:
                    return await RemoveAsync(AgentExitCodes.Stopped).ConfigureAwait(false);
                default:
                    // A failure the server did not get, or a refused token: the next registration decides, and the
                    // failure goes out if the run cannot go on.
                    unsent = result.UnsentFailure;

                    continue;
            }
        }
    }

    // Tries until the server answers, as the network may still be coming up while Windows starts. Null when the server
    // has nothing for this service to go on with.
    private async Task<AgentRegistrationResult?> RegisterAsync(string? runToken, CancellationToken cancellationToken)
    {
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
                        Environment: AgentEnvironment.Windows),
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
            }
        }
    }

    // False when the wait ended before setup finished because the server did not take the machine's report, which the
    // next registration sorts out.
    private async Task<bool> WaitForSetupAsync(Guid machineId, LocalRun local, DeploymentTokens tokens, CancellationToken cancellationToken)
    {
        if (setup.Pending() is not { } pending)
        {
            return true;
        }

        log.Information($"Waiting for Windows setup to finish: {pending}. The run goes on once it has, however long that takes.");

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
            log.Warning(ServerCallRules.Reason(exception, "the failure report"));

            return true;
        }
        catch (Exception exception) when (exception is AgentTokenRejectedException || ServerCallRules.IsTransient(exception, cancellationToken))
        {
            log.Warning($"The failure could not be reported ({exception.Message}). Trying again.");

            return false;
        }
    }

    // The run's state and answer file go first, token first, whatever is still there.
    private async Task<int> RunIsOverAsync(Guid runId, CancellationToken cancellationToken)
    {
        log.Information($"The server no longer runs run {runId} on this machine.");

        if (await LocalRun.LoadAsync(windowsRoot, log, cancellationToken).ConfigureAwait(false) is { } local)
        {
            local.Discard(log);
        }

        return await RemoveAsync(AgentExitCodes.Stopped).ConfigureAwait(false);
    }

    // Nothing may stop the removal half way.
    private async Task<int> RemoveAsync(int exitCode)
    {
        await removal.RemoveAsync(CancellationToken.None).ConfigureAwait(false);

        return exitCode;
    }

    private static AgentRunReport Failed(string error) =>
        new(DeploymentState.Failed, SequencePhase.Windows, [], null, 0, RunActivity.Preparing, error);
}
