// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Runs a task sequence in Windows PE. A fresh run is checked first, so nothing is erased for a run that cannot
// succeed; a run found on the disk after a restart goes on where it was. The engine runs the steps, and the Windows
// PE phase ends in a restart back into Windows PE for the rest of the run, or in the end of the run, after which the
// machine restarts into the Windows it installed. Nothing it does may end the agent: every failure is reported,
// because an agent that crashes is replaced by the boot image's. In a dry run, workDirectory is the dry run's root,
// which stands in for the disk and is deleted when the run ends. systemDirectory is where Windows PE keeps its tools.
public sealed class SequenceRunner(
    IAgentServer server,
    IDiskPartitioner partitioner,
    IImageApplier applier,
    IBcdWriter bcdWriter,
    IRebooter rebooter,
    IToolRunner tools,
    AgentLog log,
    TimeProvider timeProvider,
    TimeSpan heartbeatInterval,
    string workDirectory,
    string systemDirectory,
    bool dryRun)
{
    public const string NoDiskMessage =
        "No internal disk was found. If this PC's storage is set to RAID or Intel VMD/RST, switch it to AHCI in the " +
        "firmware setup: DDT's boot image has no driver for it.";

    public const string SeveralDisksMessage =
        "This machine has more than one disk DDT could install on. Restart it from the network and sign in at it to " +
        "choose the disk.";

    public const string LostContactMessage = "The agent lost contact with the server during the run.";

    // 2 GB more keeps the downloads and the applied image from filling the disk to the last byte.
    private const long SpareBytes = 2048L * 1024 * 1024;
    private const long Megabyte = 1024L * 1024;

    private const int MaxFinalFlushes = 20;
    private const int MaxFinalFlushFailures = 3;

    // Whether this run put Windows Boot Manager first, which a run that does not finish puts back.
    private bool _windowsFirst;

    // confirmedDisk is the disk the technician confirmed with ERASE in this process, if any. Disk numbers can change
    // when the machine starts again, so a run chosen at the machine only erases that same disk. resumed is the run's
    // state found on the disk, for a run that goes on after a restart.
    public async Task<RunResult> RunAsync(
        Guid machineId,
        AgentRun run,
        LocalRun? resumed,
        LocalDisk? confirmedDisk,
        DeploymentTokens tokens,
        MachineIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(identity);

        _windowsFirst = false;

        RunSession session = new(machineId, run, tokens);
        FileRunStateStore? store = null;
        RunHeartbeat heartbeat = new(
            server,
            log,
            tokens,
            machineId,
            run.Id,
            call => store?.SaveTokenAsync(call) ?? Task.CompletedTask,
            heartbeatInterval,
            timeProvider);
        store = new FileRunStateStore(tokens, heartbeat.Update);

        SequenceState state = resumed?.State ?? SequenceStates.Start(run.Id, run.Sequence);
        heartbeat.Update(state);

        MachineVariables machine = new(
            identity.Manufacturer,
            identity.Model,
            identity.SerialNumber,
            identity.SmbiosUuid,
            identity.MacAddresses,
            run.ComputerName,
            SequencePhase.WindowsPE);

        RunResult result = await RunCoreAsync(session, resumed, confirmedDisk, store, heartbeat, state, machine, cancellationToken)
            .ConfigureAwait(false);

        if (dryRun && result.Outcome is RunOutcome.Finished or RunOutcome.Failed)
        {
            Leftovers.Delete(workDirectory, log);
        }

        return result;
    }

    private async Task<RunResult> RunCoreAsync(
        RunSession session,
        LocalRun? resumed,
        LocalDisk? confirmedDisk,
        FileRunStateStore store,
        RunHeartbeat heartbeat,
        SequenceState state,
        MachineVariables machine,
        CancellationToken cancellationToken)
    {
        AgentRun run = session.Run;
        int count = run.Sequence.Steps.Count;

        try
        {
            if (resumed is null)
            {
                log.Information($"The run of {run.SequenceName} begins: {count} steps.");
                await PreflightAsync(session, confirmedDisk, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                log.Information($"The run of {run.SequenceName} goes on at step {Math.Min(state.NextIndex + 1, count)} of {count}.");
                await ResumeAsync(session, resumed, store, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (AgentTokenRejectedException)
        {
            log.Warning("The server no longer accepts this machine's token. Registering again; nothing was changed on any disk.");

            return new RunResult(RunOutcome.TokenRejected);
        }
        catch (Exception exception)
        {
            string message = LogText.OneLine(exception);
            log.Error(resumed is null ? $"The run cannot start: {message}" : $"The run cannot go on: {message}");
            resumed?.Discard(log);

            return await ReportFailedAsync(heartbeat, message, cancellationToken).ConfigureAwait(false);
        }

        // Nothing is changed on any disk before the server has the run as running. A run that goes on is running
        // already, and the heartbeat's first beat tells the server where it is.
        try
        {
            if (resumed is null)
            {
                await ServerCallRules.CallAsync(
                    call => heartbeat.ReportAsync(heartbeat.Snapshot(DeploymentState.Running), call),
                    "the start of the run",
                    log,
                    timeProvider,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (AgentTokenRejectedException)
        {
            log.Warning("The server no longer accepts this machine's token. Registering again; nothing was changed on any disk.");

            return new RunResult(RunOutcome.TokenRejected);
        }
        catch (Exception exception)
        {
            log.Error($"The server did not let the run start: {LogText.OneLine(exception)} Nothing was changed on any disk.");

            return new RunResult(RunOutcome.Failed);
        }

        return await RunStepsAsync(session, store, heartbeat, state, machine, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RunResult> RunStepsAsync(
        RunSession session,
        FileRunStateStore store,
        RunHeartbeat heartbeat,
        SequenceState state,
        MachineVariables machine,
        CancellationToken cancellationToken)
    {
        SequenceEngine engine = new(Steps(session, store, heartbeat), store, heartbeat);
        SequenceOutcome outcome = SequenceOutcome.Failed;
        string? error = null;

        // The engine saved the step that asked for a restart as done, so a resume goes on after it: once that is on the
        // disk, only the restart itself keeps the next steps from running without it.
        bool restartDue = false;

        using CancellationTokenSource steps = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        heartbeat.Activity = RunActivity.Step;
        heartbeat.Start(steps, cancellationToken);

        try
        {
            SequenceRunResult result = await engine.RunAsync(state, machine, steps.Token).ConfigureAwait(false);
            state = result.State;
            outcome = result.Outcome;
            error = result.Error;
            restartDue = outcome == SequenceOutcome.RebootRequired;

            switch (outcome)
            {
                case SequenceOutcome.Completed:
                    heartbeat.Activity = RunActivity.Finishing;
                    await MakeBootableAsync(session, state, steps.Token).ConfigureAwait(false);
                    break;
                case SequenceOutcome.RebootRequired:
                    heartbeat.Activity = RunActivity.Restarting;
                    await store.SaveTokenAsync(CancellationToken.None).ConfigureAwait(false);
                    break;
                case SequenceOutcome.PhaseChangeRequired:
                    outcome = SequenceOutcome.Failed;
                    error = "The run goes on in Windows, but this agent cannot hand it over to Windows.";
                    break;
            }

            // A beat still on its way can be refused, for example because an operator stopped the run. That still
            // ends the run, before the last reports.
            await heartbeat.StopAsync().ConfigureAwait(false);
            steps.Token.ThrowIfCancellationRequested();
        }
        catch (Exception exception)
        {
            await heartbeat.StopAsync().ConfigureAwait(false);
            outcome = SequenceOutcome.Failed;
            error = LogText.OneLine(exception);
            state = store.State ?? state;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await RestoreBootOrderAsync().ConfigureAwait(false);
            log.Warning("The agent was stopped. The run's state stays on the disk, so the run goes on after a restart if the server still runs it.");

            return new RunResult(RunOutcome.Stopped);
        }

        if (heartbeat.Failure is AgentTokenRejectedException && restartDue)
        {
            log.Warning("The server no longer accepts this machine's token. The machine restarts as the run asked, and the run goes on after the restart if the server still runs it.");

            return await RebootAsync(RestartInto.WindowsPE, RunOutcome.Restarting, "Restart it by hand; the run goes on after the restart.", cancellationToken)
                .ConfigureAwait(false);
        }

        if (heartbeat.Failure is AgentTokenRejectedException)
        {
            // The run's state and answer file stay: the registration with the run token decides whether it goes on.
            await RestoreBootOrderAsync().ConfigureAwait(false);
            log.Warning("The server no longer accepts this machine's token during the run. Registering again.");

            return new RunResult(RunOutcome.TokenRejected, heartbeat.Snapshot(DeploymentState.Failed, LostContactMessage));
        }

        if (heartbeat.Failure is { } refusal)
        {
            return await FailAsync(session, store, heartbeat, state, LogText.OneLine(refusal), cancellationToken).ConfigureAwait(false);
        }

        return outcome switch
        {
            SequenceOutcome.Completed => await FinishAsync(session, store, heartbeat, state, cancellationToken).ConfigureAwait(false),
            SequenceOutcome.RebootRequired => await RestartAsync(heartbeat, cancellationToken).ConfigureAwait(false),
            SequenceOutcome.Stopped => new RunResult(RunOutcome.Stopped),
            _ => await FailAsync(session, store, heartbeat, state, error ?? "The run failed.", cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task PreflightAsync(RunSession session, LocalDisk? confirmedDisk, CancellationToken cancellationToken)
    {
        AgentRun run = session.Run;

        // The server checked the sequence when it was saved; this agent checks it again against what it can run.
        if (SequenceValidator.Validate(run.Sequence) is [var problem, ..])
        {
            throw new DeploymentStepException($"{run.SequenceName} cannot run: {problem.Message} Correct the sequence and assign it again.");
        }

        IReadOnlyList<SequenceStep> steps = run.Sequence.Steps;
        List<AgentRunImage> images = [];

        foreach (ApplyImageStep step in steps.OfType<ApplyImageStep>())
        {
            images.Add(run.Images.FirstOrDefault(image => image.ImageId == step.ImageId)
                ?? throw new DeploymentStepException($"The server sent no image for step {step.Name}. Assign the sequence again."));
        }

        if (steps.OfType<InjectDriversStep>().Any() && !File.Exists(InjectDriversStepRunner.DismIn(systemDirectory)))
        {
            throw new DeploymentStepException(InjectDriversStepRunner.NoDismMessage);
        }

        if (steps.OfType<RunScriptStep>().Any(step => step is { Interpreter: ScriptInterpreter.PowerShell, Phase: SequencePhase.WindowsPE })
            && !File.Exists(RunScriptStepRunner.PowerShellIn(systemDirectory)))
        {
            throw new DeploymentStepException(RunScriptStepRunner.NoPowerShellMessage);
        }

        if (steps.Any(step => step.ErasesDisk))
        {
            IReadOnlyList<LocalDisk> disks = await partitioner.ListDisksAsync(cancellationToken).ConfigureAwait(false);
            LocalDisk disk = SelectDisk(run.DiskNumber, confirmedDisk, disks);
            CheckSize(run, disk, images, steps.OfType<PartitionStep>().FirstOrDefault());
            session.Disk = disk;
        }

        if (images.Count > 0)
        {
            applier.Prepare();
        }

        foreach (AgentRunImage image in images)
        {
            long? length = await ServerCallRules.CallAsync(
                call => server.HeadRunFileAsync(session.MachineId, session.Tokens.Token, run.Id, image.Sha256, call),
                "the image check",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);

            if (length is null)
            {
                log.Warning($"The server did not say how large {image.Name} is. The download checks it instead.");
            }
            else if (length != image.SizeBytes)
            {
                throw new DeploymentStepException(
                    $"The server's file for {image.Name} holds {length} bytes instead of {image.SizeBytes}. Upload the image again.");
            }
        }

        log.Information(session.Disk is { } chosen ? $"Running {run.SequenceName} on {chosen.Describe()}." : $"Running {run.SequenceName}.");
    }

    // The size rule: the partitions, each image's download and installed files, each package with room to unpack it,
    // and some to spare.
    private static void CheckSize(AgentRun run, LocalDisk disk, IReadOnlyList<AgentRunImage> images, PartitionStep? partition)
    {
        int system = partition?.SystemPartitionMegabytes ?? DiskpartScript.SystemPartitionMegabytes;
        int recovery = partition?.RecoveryPartitionMegabytes ?? DiskpartScript.RecoveryPartitionMegabytes;
        long partitions = (system + DiskpartScript.ReservedPartitionMegabytes + recovery) * Megabyte;
        long downloads = images.Sum(image => image.SizeBytes);
        long installed = images.Sum(image => image.InstalledBytes);
        long packages = 2 * run.Packages.Sum(package => package.SizeBytes);
        long required = partitions + downloads + installed + packages + SpareBytes;

        if (disk.SizeBytes >= required)
        {
            return;
        }

        List<string> parts = [$"{ByteSize.Format(partitions)} for the boot and recovery partitions"];

        if (images.Count > 0)
        {
            parts.Add($"{ByteSize.Format(downloads)} for the image downloads");
            parts.Add($"{ByteSize.Format(installed)} for the installed files");
        }

        if (packages > 0)
        {
            parts.Add($"{ByteSize.Format(packages)} for the packages and their contents");
        }

        throw new DeploymentStepException(
            $"Disk {disk.Number} holds {ByteSize.Format(disk.SizeBytes)}, but {run.SequenceName} needs {ByteSize.Format(required)}: " +
            $"{string.Join(", ", parts)} and {ByteSize.Format(SpareBytes)} to spare. Run it on a larger disk.");
    }

    private static LocalDisk SelectDisk(int? diskNumber, LocalDisk? confirmedDisk, IReadOnlyList<LocalDisk> disks)
    {
        if (diskNumber is { } number)
        {
            if (confirmedDisk is null || confirmedDisk.Number != number)
            {
                throw new DeploymentStepException(
                    $"Disk {number} was chosen before the agent started again, and disk numbers can change when a machine restarts, " +
                    "so nothing was erased. Choose the sequence and the disk again at this machine.");
            }

            LocalDisk disk = disks.FirstOrDefault(candidate => candidate.Number == number)
                ?? throw new DeploymentStepException(
                    $"Disk {number}, chosen at this machine, is not a disk DDT can install on now. Restart the machine from the network and choose again.");

            if (!disk.IsSameDiskAs(confirmedDisk))
            {
                throw new DeploymentStepException(
                    $"Disk {number} is no longer the disk chosen at this machine ({confirmedDisk.DisplayModel}, " +
                    $"{ByteSize.Format(confirmedDisk.SizeBytes)}), so nothing was erased. Choose the sequence and the disk again at this machine.");
            }

            return disk;
        }

        return disks.Count switch
        {
            0 => throw new DeploymentStepException(NoDiskMessage),
            1 => disks[0],
            _ => throw new DeploymentStepException(SeveralDisksMessage),
        };
    }

    // Windows PE lettered the run's Windows volume as it chose, and the other partitions not at all. Before Partition
    // finished, the state has no partition ids, and the engine fails the interrupted Partition.
    private async Task ResumeAsync(RunSession session, LocalRun resumed, FileRunStateStore store, CancellationToken cancellationToken)
    {
        if (RunVariables.DiskIds(resumed.State.Variables) is { } ids)
        {
            session.Volumes = await partitioner.FindAsync(ids, resumed.WindowsRoot, cancellationToken).ConfigureAwait(false);
        }

        await store.AttachAsync(resumed.Files, CancellationToken.None).ConfigureAwait(false);
    }

    private AgentStepRunner Steps(RunSession session, FileRunStateStore store, RunHeartbeat heartbeat)
    {
        RunDownloads downloads = new(server, session, log, timeProvider, heartbeatInterval);

        return new AgentStepRunner(
            new PartitionStepRunner(partitioner, session, store, log, dryRun),
            new ApplyImageStepRunner(applier, downloads, session, log),
            new InjectDriversStepRunner(tools, downloads, session, log),
            new WriteUnattendStepRunner(server, session, heartbeat.ReportNowAsync, log, timeProvider),
            new RunScriptStepRunner(tools, downloads, session, log, workDirectory),
            heartbeat.TokenRejected,
            log,
            timeProvider);
    }

    // As in Microsoft's sequence after applying: the applied image's bcdboot and its recovery environment, and then,
    // last, Windows Boot Manager first in the firmware's boot order: until then a restart starts the machine from the
    // network, not into a Windows that is not finished.
    private async Task MakeBootableAsync(RunSession session, SequenceState state, CancellationToken cancellationToken)
    {
        if (!state.Variables.TryGetValue(RunVariables.WindowsApplied, out string? applied) || applied != RunVariables.Set)
        {
            return;
        }

        TargetVolumes volumes = session.RequireVolumes();
        await bcdWriter.WriteAsync(volumes, cancellationToken).ConfigureAwait(false);
        _windowsFirst = true;
        await bcdWriter.PutWindowsFirstAsync(volumes, cancellationToken).ConfigureAwait(false);
    }

    // The order matters: the log goes while the machine may still send it, and the Done report is the last call the
    // server gets. The restart follows whatever the Done report answers: the run is over either way.
    private async Task<RunResult> FinishAsync(
        RunSession session,
        FileRunStateStore store,
        RunHeartbeat heartbeat,
        SequenceState state,
        CancellationToken cancellationToken)
    {
        log.Information("The run is done. Sending the last log lines, then restarting.");

        // The token first, so nothing left on the disk can act as the machine.
        store.Files?.Discard();

        if (session.RunDirectory is { } directory)
        {
            Leftovers.Delete(directory, log);
        }

        try
        {
            // Refused before the Done report was sent: the run was stopped meanwhile, so the machine must not start
            // into a Windows with the answer file.
            if (!await FlushAllAsync(heartbeat, cancellationToken).ConfigureAwait(false))
            {
                await UndoAsync(session, state).ConfigureAwait(false);
                log.Warning("The server no longer accepts this machine's token, so the run was stopped before it ended. Registering again.");

                return new RunResult(RunOutcome.TokenRejected);
            }

            await ServerCallRules.CallAsync(
                call => heartbeat.ReportAsync(heartbeat.Snapshot(DeploymentState.Done), call),
                "the end of the run",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (AgentTokenRejectedException)
        {
            // The server stores Done before it answers, so a lost answer makes the retry look like this.
            log.Warning("The server no longer accepts this machine's token. It most likely recorded the run as done already.");
        }
        catch (Exception exception)
        {
            log.Warning($"The server could not be told that the run is done ({LogText.OneLine(exception)}). The machine restarts anyway.");
        }

        return await RebootAsync(RestartInto.Windows, RunOutcome.Finished, "Restart it by hand; the run is done.", cancellationToken)
            .ConfigureAwait(false);
    }

    // The run's state and token are on the disk, and the rest of the run follows the next start of Windows PE.
    private async Task<RunResult> RestartAsync(RunHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        log.Information("The machine restarts into Windows PE, and the run goes on after the restart.");
        await FlushAllAsync(heartbeat, cancellationToken).ConfigureAwait(false);

        try
        {
            await ServerCallRules.CallAsync(
                call => heartbeat.ReportAsync(heartbeat.Snapshot(DeploymentState.Running), call),
                "the restart",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (Exception exception)
        {
            // The registration after the restart tells whether the run goes on.
            log.Warning($"The server could not be told of the restart ({LogText.OneLine(exception)}). The machine restarts anyway.");
        }

        return await RebootAsync(RestartInto.WindowsPE, RunOutcome.Restarting, "Restart it by hand; the run goes on after the restart.", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<RunResult> RebootAsync(RestartInto into, RunOutcome outcome, string byHand, CancellationToken cancellationToken)
    {
        try
        {
            await rebooter.RebootAsync(into, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (Exception exception)
        {
            log.Error($"The machine could not restart itself ({LogText.OneLine(exception)}). {byHand}");
        }

        return new RunResult(outcome);
    }

    // The run is over: nothing of it may start Windows or resume it.
    private async Task<RunResult> FailAsync(
        RunSession session,
        FileRunStateStore store,
        RunHeartbeat heartbeat,
        SequenceState state,
        string error,
        CancellationToken cancellationToken)
    {
        log.Error($"The run failed: {error}");
        await UndoAsync(session, state).ConfigureAwait(false);
        store.Files?.Discard();
        await FlushAllAsync(heartbeat, cancellationToken).ConfigureAwait(false);

        return await ReportFailedAsync(heartbeat, error, cancellationToken).ConfigureAwait(false);
    }

    // A failure the server was not told about is handed back, for the loop to report once it can.
    private async Task<RunResult> ReportFailedAsync(RunHeartbeat heartbeat, string error, CancellationToken cancellationToken)
    {
        AgentRunReport report = heartbeat.Snapshot(DeploymentState.Failed, error);

        try
        {
            await ServerCallRules.CallAsync(
                call => heartbeat.ReportAsync(report, call),
                "the failure report",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);

            return new RunResult(RunOutcome.Failed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (AgentTokenRejectedException)
        {
            log.Warning("The server no longer accepts this machine's token, so the failure could not be reported. Registering again.");

            return new RunResult(RunOutcome.TokenRejected, report);
        }
        catch (Exception exception)
        {
            log.Warning($"The failure could not be reported ({LogText.OneLine(exception)}).");

            return new RunResult(RunOutcome.Failed, report);
        }
    }

    // The answer file holds passwords, and a machine whose run did not finish keeps its disk until it runs again: it
    // must start from the network, not into a Windows without its answer file. The boot order goes back even after a
    // stop.
    private async Task UndoAsync(RunSession session, SequenceState state)
    {
        if (session.Volumes is { } volumes)
        {
            LocalRun.DeleteAnswerFile(state, volumes.Windows, log);
        }

        await RestoreBootOrderAsync().ConfigureAwait(false);
    }

    private async Task RestoreBootOrderAsync()
    {
        if (_windowsFirst)
        {
            _windowsFirst = false;
            await bcdWriter.RestoreBootOrderAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    // Until the queue is empty, but never for long: the lines are worth less than the restart that follows. False
    // when the server refused the token.
    private async Task<bool> FlushAllAsync(RunHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        int failures = 0;

        for (int attempt = 0; attempt < MaxFinalFlushes && log.QueuedLines > 0; attempt++)
        {
            try
            {
                await heartbeat.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (AgentTokenRejectedException)
            {
                return false;
            }
            catch (Exception exception) when (ServerCallRules.IsRefusal(exception))
            {
                return true;
            }
            catch (Exception exception) when (ServerCallRules.IsTransient(exception, cancellationToken))
            {
                if (++failures > MaxFinalFlushFailures)
                {
                    return true;
                }

                await Task.Delay(AgentLimits.RetryDelay(failures), timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return true;
            }
        }

        return true;
    }
}
