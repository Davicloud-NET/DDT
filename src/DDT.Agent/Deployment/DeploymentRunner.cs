// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;

namespace DDT.Agent.Deployment;

// Runs one deployment: checks that it can succeed before touching the disk, then partitions, downloads, applies,
// makes Windows bootable, writes the answer file, puts Windows first in the firmware boot order and restarts.
// Nothing it does may end the agent: every failure is reported, because an agent that crashes is replaced by the
// boot image's, which starts the machine over.
public sealed class DeploymentRunner(
    IAgentServer server,
    IDiskPartitioner partitioner,
    IImageApplier applier,
    IBcdWriter bcdWriter,
    IRebooter rebooter,
    AgentLog log,
    TimeProvider timeProvider,
    TimeSpan heartbeatInterval,
    string? scratchDirectory = null)
{
    public const string NoDiskMessage =
        "No internal disk was found. If this PC's storage is set to RAID or Intel VMD/RST, switch it to AHCI in the " +
        "firmware setup: DDT's boot image has no driver for it.";

    public const string SeveralDisksMessage =
        "This machine has more than one disk DDT could install on. Restart it from the network and sign in at it to " +
        "choose the disk.";

    // The system, reserved and recovery partitions take about 1.5 GB; 2 GB more keeps the download and the applied
    // image from filling the disk to the last byte.
    private const long PartitionBytes = 1536L * 1024 * 1024;
    private const long SpareBytes = 2048L * 1024 * 1024;

    private const int MaxFinalFlushes = 20;
    private const int MaxFinalFlushFailures = 3;
    private const int MaxErrorLength = 1024;

    // The current run's answer file, from the moment it is written.
    private string? _answerFile;

    // confirmedDisk is the disk the technician confirmed with ERASE in this process, if any. Disk numbers can change
    // when the machine starts again, so a deployment chosen at the machine only runs on that same disk.
    public async Task<DeploymentRunResult> RunAsync(
        Guid machineId,
        AgentDeployment deployment,
        LocalDisk? confirmedDisk,
        string token,
        string resumeToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        _answerFile = null;
        DeploymentTokens tokens = new(token, resumeToken);
        DeploymentHeartbeat heartbeat = new(server, log, tokens, machineId, deployment.Id, heartbeatInterval, timeProvider);

        try
        {
            (DeploymentOutcome outcome, string? unsentError) = await RunCoreAsync(machineId, deployment, confirmedDisk, tokens, heartbeat, cancellationToken)
                .ConfigureAwait(false);

            return new DeploymentRunResult(outcome, tokens.Token, tokens.ResumeToken, heartbeat.Step, heartbeat.Percent, unsentError);
        }
        finally
        {
            DeleteScratchDirectory();
        }
    }

    private async Task<(DeploymentOutcome Outcome, string? UnsentError)> RunCoreAsync(
        Guid machineId,
        AgentDeployment deployment,
        LocalDisk? confirmedDisk,
        DeploymentTokens tokens,
        DeploymentHeartbeat heartbeat,
        CancellationToken cancellationToken)
    {
        log.Information($"Deployment of {deployment.ImageName} ({ByteSize.Format(deployment.SizeBytes)}) begins.");

        LocalDisk disk;

        try
        {
            disk = await PreflightAsync(machineId, deployment, confirmedDisk, tokens, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (DeploymentOutcome.Stopped, null);
        }
        catch (AgentTokenRejectedException)
        {
            log.Warning("The server no longer accepts this machine's token. Registering again; nothing was changed on any disk.");

            return (DeploymentOutcome.TokenRejected, null);
        }
        catch (Exception exception)
        {
            string message = OneLine(exception);
            log.Error($"The deployment cannot start: {message}");

            return await ReportFailedAsync(heartbeat, DeploymentStep.Partition, 0, message, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await ServerCallRules.CallAsync(
                call => heartbeat.ReportAsync(new AgentDeploymentReport(DeploymentState.Running, DeploymentStep.Partition, 0, null), call),
                "the start of the deployment",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (DeploymentOutcome.Stopped, null);
        }
        catch (AgentTokenRejectedException)
        {
            log.Warning("The server no longer accepts this machine's token. Registering again; nothing was changed on any disk.");

            return (DeploymentOutcome.TokenRejected, null);
        }
        catch (Exception exception)
        {
            log.Error($"The server did not let the deployment start: {OneLine(exception)} Nothing was changed on disk {disk.Number}.");

            return (DeploymentOutcome.Failed, null);
        }

        using CancellationTokenSource steps = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        heartbeat.Start(steps, cancellationToken);

        try
        {
            await RunStepsAsync(machineId, deployment, disk, tokens, heartbeat, steps.Token).ConfigureAwait(false);

            // A beat still on its way can be refused, for example because an operator stopped the deployment. That
            // still ends the run, before the Done report.
            await heartbeat.StopAsync().ConfigureAwait(false);
            steps.Token.ThrowIfCancellationRequested();
        }
        catch (Exception exception)
        {
            await heartbeat.StopAsync().ConfigureAwait(false);
            await UndoUnattendAsync().ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                return (DeploymentOutcome.Stopped, null);
            }

            Exception failure = heartbeat.Failure ?? exception;

            if (failure is AgentTokenRejectedException)
            {
                log.Warning("The server no longer accepts this machine's token during the deployment. Registering again.");

                return (DeploymentOutcome.TokenRejected, null);
            }

            string message = OneLine(failure);
            log.Error($"The deployment failed at step {heartbeat.Step}: {message}");
            await FlushAllAsync(heartbeat, cancellationToken).ConfigureAwait(false);

            return await ReportFailedAsync(heartbeat, heartbeat.Step, heartbeat.Percent, message, cancellationToken).ConfigureAwait(false);
        }

        return (await FinishAsync(heartbeat, cancellationToken).ConfigureAwait(false), null);
    }

    private async Task<LocalDisk> PreflightAsync(
        Guid machineId,
        AgentDeployment deployment,
        LocalDisk? confirmedDisk,
        DeploymentTokens tokens,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalDisk> disks = await partitioner.ListDisksAsync(cancellationToken).ConfigureAwait(false);
        LocalDisk disk = SelectDisk(deployment, confirmedDisk, disks);
        long required = PartitionBytes + deployment.SizeBytes + deployment.InstalledBytes + SpareBytes;

        if (disk.SizeBytes < required)
        {
            throw new DeploymentStepException(
                $"Disk {disk.Number} holds {ByteSize.Format(disk.SizeBytes)}, but {deployment.ImageName} needs {ByteSize.Format(required)}: " +
                $"{ByteSize.Format(PartitionBytes)} for the boot and recovery partitions, {ByteSize.Format(deployment.SizeBytes)} for the " +
                $"download, {ByteSize.Format(deployment.InstalledBytes)} for the installed files and {ByteSize.Format(SpareBytes)} to spare. " +
                "Install it on a larger disk.");
        }

        applier.Prepare();

        long? length = await ServerCallRules.CallAsync(
            call => server.HeadImageAsync(machineId, tokens.Token, deployment.Sha256, call),
            "the image check",
            log,
            timeProvider,
            cancellationToken).ConfigureAwait(false);

        if (length is null)
        {
            log.Warning("The server did not say how large the image is. The download checks it instead.");
        }
        else if (length != deployment.SizeBytes)
        {
            throw new DeploymentStepException(
                $"The server's file for {deployment.ImageName} holds {length} bytes instead of {deployment.SizeBytes}. Upload the image again.");
        }

        log.Information($"Installing {deployment.ImageName} on {disk.Describe()}.");

        return disk;
    }

    private static LocalDisk SelectDisk(AgentDeployment deployment, LocalDisk? confirmedDisk, IReadOnlyList<LocalDisk> disks)
    {
        if (deployment.DiskNumber is { } number)
        {
            if (confirmedDisk is null || confirmedDisk.Number != number)
            {
                throw new DeploymentStepException(
                    $"Disk {number} was chosen before the agent started again, and disk numbers can change when a machine restarts, " +
                    "so nothing was installed. Choose the image and the disk again at this machine.");
            }

            LocalDisk disk = disks.FirstOrDefault(candidate => candidate.Number == number)
                ?? throw new DeploymentStepException(
                    $"Disk {number}, chosen at this machine, is not a disk DDT can install on now. Restart the machine from the network and choose again.");

            if (!disk.IsSameDiskAs(confirmedDisk))
            {
                throw new DeploymentStepException(
                    $"Disk {number} is no longer the disk chosen at this machine ({confirmedDisk.DisplayModel}, " +
                    $"{ByteSize.Format(confirmedDisk.SizeBytes)}), so nothing was installed. Choose the image and the disk again at this machine.");
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

    private async Task RunStepsAsync(
        Guid machineId,
        AgentDeployment deployment,
        LocalDisk disk,
        DeploymentTokens tokens,
        DeploymentHeartbeat heartbeat,
        CancellationToken cancellationToken)
    {
        TargetVolumes volumes = await StepAsync(heartbeat, DeploymentStep.Partition, () => partitioner.PartitionAsync(disk, cancellationToken))
            .ConfigureAwait(false);

        string cache = Path.Combine(volumes.Windows, "DDT");
        string sha256 = deployment.Sha256.ToLowerInvariant();
        string wim = Path.Combine(cache, $"{sha256}.wim");

        await StepAsync(heartbeat, DeploymentStep.Download, async () =>
        {
            Directory.CreateDirectory(cache);
            ImageDownloader downloader = new(server, tokens, log, timeProvider, heartbeatInterval);

            await downloader.DownloadAsync(
                machineId,
                sha256,
                deployment.SizeBytes,
                Path.Combine(cache, $"{sha256}.part"),
                wim,
                new StepProgress(heartbeat, DeploymentStep.Download),
                cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        await StepAsync(heartbeat, DeploymentStep.Apply, async () =>
        {
            await applier.ApplyAsync(wim, deployment.WimIndex, volumes.Windows, new StepProgress(heartbeat, DeploymentStep.Apply), cancellationToken)
                .ConfigureAwait(false);

            // The image must not stay on the disk Windows boots from.
            Directory.Delete(cache, recursive: true);
        }).ConfigureAwait(false);

        await StepAsync(heartbeat, DeploymentStep.Boot, () => bcdWriter.WriteAsync(volumes, cancellationToken)).ConfigureAwait(false);

        await StepAsync(heartbeat, DeploymentStep.Unattend, async () =>
        {
            string unattend = await ServerCallRules.CallAsync(
                call => server.GetUnattendAsync(machineId, tokens.Token, deployment.Id, call),
                "the unattend file",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);

            // Never logged as it is: it holds passwords.
            string summary = UnattendFile.Summarize(unattend);

            // From here on, a failure or a stop deletes the answer file again and puts the boot order back.
            _answerFile = UnattendFile.PathIn(volumes.Windows);
            await UnattendFile.WriteAsync(volumes.Windows, unattend, cancellationToken).ConfigureAwait(false);
            log.Information($"Wrote the unattend file: {summary}.");

            // Last: a failure or a restart before this point still starts the machine from the network, not into a
            // Windows without its answer file.
            await bcdWriter.PutWindowsFirstAsync(volumes, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private Task StepAsync(DeploymentHeartbeat heartbeat, DeploymentStep step, Func<Task> run) =>
        StepAsync(heartbeat, step, async () =>
        {
            await run().ConfigureAwait(false);

            return true;
        });

    private async Task<T> StepAsync<T>(DeploymentHeartbeat heartbeat, DeploymentStep step, Func<Task<T>> run)
    {
        heartbeat.Progress(step, 0);
        log.Information($"Step {step} begins.");
        long started = timeProvider.GetTimestamp();

        T result = await run().ConfigureAwait(false);

        heartbeat.Progress(step, 100);
        log.Information($"Step {step} finished after {Duration(timeProvider.GetElapsedTime(started))}.");

        return result;
    }

    // The order matters: the heartbeat has stopped so nothing overlaps the last calls, the log goes while the
    // machine may still send it, and the Done report is the last call the server gets. The restart follows
    // whatever the Done report answers: Windows is on the disk either way.
    private async Task<DeploymentOutcome> FinishAsync(DeploymentHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        heartbeat.Progress(DeploymentStep.Reboot, 100);
        log.Information("Windows is installed. Sending the last log lines, then restarting into Windows.");

        try
        {
            // Refused before the Done report was sent: the deployment was stopped meanwhile, so Windows must not
            // start with the answer file.
            if (!await FlushAllAsync(heartbeat, cancellationToken).ConfigureAwait(false))
            {
                await UndoUnattendAsync().ConfigureAwait(false);
                log.Warning("The server no longer accepts this machine's token, so the deployment was stopped before it ended. Registering again.");

                return DeploymentOutcome.TokenRejected;
            }

            await ServerCallRules.CallAsync(
                call => heartbeat.ReportAsync(new AgentDeploymentReport(DeploymentState.Done, DeploymentStep.Reboot, 100, null), call),
                "the end of the deployment",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DeploymentOutcome.Stopped;
        }
        catch (AgentTokenRejectedException)
        {
            // The server stores Done before it answers, so a lost answer makes the retry look like this.
            log.Warning("The server no longer accepts this machine's token. It most likely recorded the deployment as done already.");
        }
        catch (Exception exception)
        {
            log.Warning($"The server could not be told that the deployment is done ({OneLine(exception)}). The machine restarts anyway.");
        }

        try
        {
            await rebooter.RebootAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DeploymentOutcome.Stopped;
        }
        catch (Exception exception)
        {
            log.Error($"The machine could not restart itself ({OneLine(exception)}). Restart it by hand; Windows is installed.");
        }

        return DeploymentOutcome.Deployed;
    }

    // A failure the server was not told about is handed back, for the loop to report once it can.
    private async Task<(DeploymentOutcome Outcome, string? UnsentError)> ReportFailedAsync(
        DeploymentHeartbeat heartbeat,
        DeploymentStep step,
        int percent,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await ServerCallRules.CallAsync(
                call => heartbeat.ReportAsync(new AgentDeploymentReport(DeploymentState.Failed, step, percent, message), call),
                "the failure report",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);

            return (DeploymentOutcome.Failed, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (DeploymentOutcome.Stopped, null);
        }
        catch (AgentTokenRejectedException)
        {
            log.Warning("The server no longer accepts this machine's token, so the failure could not be reported. Registering again.");

            return (DeploymentOutcome.TokenRejected, message);
        }
        catch (Exception exception)
        {
            log.Warning($"The failure could not be reported ({OneLine(exception)}).");

            return (DeploymentOutcome.Failed, message);
        }
    }

    // Until the queue is empty, but never for long: the lines are worth less than the restart that follows. False
    // when the server refused the token.
    private async Task<bool> FlushAllAsync(DeploymentHeartbeat heartbeat, CancellationToken cancellationToken)
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

    private void DeleteScratchDirectory()
    {
        if (scratchDirectory is null || !Directory.Exists(scratchDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(scratchDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"{scratchDirectory} could not be deleted ({exception.Message}). Delete it by hand: it holds the unattend file.");
        }
    }

    // The answer file holds passwords, and a machine that did not finish keeps its disk until it is deployed again:
    // it must start from the network, not into a Windows without its answer file. The boot order goes back even after
    // a stop.
    private async Task UndoUnattendAsync()
    {
        if (_answerFile is not { } path)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"{path} could not be deleted ({exception.Message}).");
        }

        await bcdWriter.RestoreBootOrderAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static string Duration(TimeSpan elapsed) =>
        elapsed.TotalMinutes >= 1 ? $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds} s" : $"{elapsed.TotalSeconds:0} s";

    private static string OneLine(Exception exception)
    {
        string message = string.Join(' ', exception.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return message.Length > MaxErrorLength ? message[..MaxErrorLength] : message;
    }
}
