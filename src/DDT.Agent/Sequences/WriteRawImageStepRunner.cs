// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Images;
using DDT.Contracts.Sequences;
using DDT.Core.Disks;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Erases the disk's partition table and writes the raw disk image over the whole disk as it downloads, unpacking it on
// the way; nothing of it is kept elsewhere, so a disk as large as the image is enough. The image's partition table goes
// on last, with its backup moved to the end of this disk. Before anything is erased, an image that will not start with
// the Secure Boot this machine has on is refused, unless the run was allowed to write it.
public sealed class WriteRawImageStepRunner(
    IDiskPartitioner partitioner,
    IRawDisks disks,
    RunDownloads downloads,
    RunSession session,
    AgentLog log)
{
    public const string FourKilobyteSectorsMessage =
        "has sectors of 4096 bytes, and DDT writes images made for disks with 512-byte sectors, which is what distributions publish. " +
        "Run the sequence on another disk.";

    public async Task<StepResult> RunAsync(WriteRawImageStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        AgentRunImage image = ImageOf(session.Run, step);
        LocalDisk disk = session.Disk
            ?? throw new DeploymentStepException("No disk was chosen for this run, so nothing was written.");

        if (SecureBootGate.Refusal(image, session.Run.AllowSecureBootMismatch, session.SecureBootEnabled, session.TrustedUefiCas) is { } refusal)
        {
            throw new DeploymentStepException(refusal);
        }

        IReadOnlyList<Guid> erased = await partitioner.CleanAsync(disk, cancellationToken).ConfigureAwait(false);
        using IRawDisk raw = disks.Open(disk);

        if (raw.SectorSize != GptLayout.SectorSize)
        {
            throw new DeploymentStepException($"Disk {disk.Number} {FourKilobyteSectorsMessage}");
        }

        RawDiskWriter writer = new(raw, log);
        using RawImageSink sink = new(writer);

        log.Information($"Writing {image.Name}, {ByteSize.Format(image.InstalledBytes)} as a disk, to {disk.Describe()} as it downloads.");
        await downloads.DownloadToAsync(image.Name, image.Sha256, image.SizeBytes, sink, context.Progress, cancellationToken).ConfigureAwait(false);

        GptLayout layout = sink.Finish();
        log.Information(
            $"{image.Name} is on disk {disk.Number}: {ByteSize.Format(writer.Written)} with {layout.Partitions.Count} partitions, " +
            "and the backup partition table at the end of the disk.");
        context.Progress.Report(100);

        return StepResult.Done(RunVariables.OfRawImage(layout, erased));
    }

    public static AgentRunImage ImageOf(AgentRun run, WriteRawImageStep step)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(step);

        return run.Images.FirstOrDefault(candidate => candidate.ImageId == step.ImageId && candidate.Kind == ImageKind.RawDisk)
            ?? throw new DeploymentStepException($"The server sent no disk image for step {step.Name}. Assign the sequence again.");
    }
}
