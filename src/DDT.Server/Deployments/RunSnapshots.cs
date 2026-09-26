// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Packages;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Sequences;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Packages;
using DDT.Server.Sequences;

namespace DDT.Server.Deployments;

// What is frozen when a run is created: a row per step and the files the steps download, resolved for the machine.
// Driver packages are matched to the machine's model now, so one uploaded later is not part of the run.
public static class RunSnapshots
{
    private const long Megabyte = 1024 * 1024;

    // The Microsoft reserved partition every Partition step makes.
    private const long ReservedPartitionBytes = 16 * Megabyte;

    public static IReadOnlyList<DeploymentStep> Steps(Guid runId, SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return
        [
            .. definition.Steps.Select((step, index) => new DeploymentStep
            {
                DeploymentId = runId,
                StepId = step.Id,
                Index = index,
                Name = StoredText.Bound(step.Name, DeploymentLimits.MaxStepNameLength) ?? "",
                Kind = Kind(step),
                Phase = SequencePhases.Of(definition, index),
                State = StepState.Pending,
            }),
        ];
    }

    public static IReadOnlyList<DeploymentArtifact> Artifacts(
        Guid runId,
        SequenceDefinition definition,
        SequenceReferences references,
        Machine machine)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(machine);

        List<DeploymentArtifact> artifacts = [];

        foreach (SequenceStep step in definition.Steps)
        {
            switch (step)
            {
                case ApplyImageStep apply when references.Images.TryGetValue(apply.ImageId, out Image? image):
                    artifacts.Add(new DeploymentArtifact
                    {
                        DeploymentId = runId,
                        StepId = step.Id,
                        Kind = ArtifactKind.Image,
                        SourceId = image.Id,
                        Name = image.Name,
                        Sha256 = image.Sha256,
                        SizeBytes = image.SizeBytes,
                        ExpandedBytes = image.InstalledBytes,
                        WimIndex = image.WimIndex,
                        Language = image.Language,
                    });
                    break;

                case WriteRawImageStep raw when references.Images.TryGetValue(raw.ImageId, out Image? disk):
                    artifacts.Add(new DeploymentArtifact
                    {
                        DeploymentId = runId,
                        StepId = step.Id,
                        Kind = ArtifactKind.Image,
                        SourceId = disk.Id,
                        Name = disk.Name,
                        Sha256 = disk.Sha256,
                        SizeBytes = disk.SizeBytes,
                        ExpandedBytes = disk.InstalledBytes,
                        BootCapability = disk.BootCapability,
                        SignedUnder = disk.SignedUnder,
                    });
                    break;

                case InjectDriversStep:
                    artifacts.AddRange(references.Packages.Values
                        .Where(p => p.Kind == PackageKind.Drivers
                            && PackageTargets.Matches(PackageTargets.Read(p), machine.Manufacturer, machine.Model))
                        .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(p => p.Id)
                        .Select(p => Package(runId, step.Id, ArtifactKind.Drivers, p)));
                    break;

                case RunScriptStep { PackageId: { } packageId } when references.Packages.TryGetValue(packageId, out Package? package):
                    artifacts.Add(Package(runId, step.Id, ArtifactKind.Files, package));
                    break;
            }
        }

        return artifacts;
    }

    // The space a run needs on the disk it erases: its partitions, and every file both downloaded and unpacked. A raw
    // disk image is written as it downloads, so only the disk it holds counts, and the seed after it.
    public static long RequiredBytes(SequenceDefinition definition, IReadOnlyList<DeploymentArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(artifacts);

        long partitions = definition.Steps
            .OfType<PartitionStep>()
            .Sum(step => ((long)step.SystemPartitionMegabytes + step.RecoveryPartitionMegabytes) * Megabyte + ReservedPartitionBytes);
        HashSet<Guid> rawSteps = [.. definition.Steps.OfType<WriteRawImageStep>().Select(step => step.Id)];
        long seed = definition.Steps.Any(step => step is WriteCloudInitSeedStep) ? CloudInitSeed.DiskBytes : 0;

        return partitions + seed + artifacts.Sum(artifact =>
            rawSteps.Contains(artifact.StepId) ? artifact.ExpandedBytes : artifact.SizeBytes + artifact.ExpandedBytes);
    }

    // Never a secret: the answer file and the join credentials are fetched while their step runs. An image a Write raw
    // disk image step names is a raw disk image, whatever the library holds by now.
    public static AgentRun ForAgent(
        Deployment run,
        SequenceDefinition definition,
        IReadOnlyList<DeploymentArtifact> artifacts,
        string? computerName)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(artifacts);

        HashSet<Guid> rawSteps = [.. definition.Steps.OfType<WriteRawImageStep>().Select(step => step.Id)];

        return new AgentRun(
            run.Id,
            run.State,
            run.Title,
            definition,
            [
                .. artifacts
                    .Where(a => a.Kind == ArtifactKind.Image)
                    .Select(a => rawSteps.Contains(a.StepId)
                        ? new AgentRunImage(a.SourceId, a.Name, a.Sha256, a.SizeBytes, 0, a.ExpandedBytes, ImageKind.RawDisk, a.BootCapability, a.SignedUnder)
                        : new AgentRunImage(a.SourceId, a.Name, a.Sha256, a.SizeBytes, a.WimIndex ?? 1, a.ExpandedBytes)),
            ],
            [.. artifacts.Where(a => a.Kind != ArtifactKind.Image).Select(a => new AgentRunPackage(a.StepId, a.Name, a.Sha256, a.SizeBytes))],
            run.DiskNumber,
            computerName,
            run.AllowSecureBootMismatch);
    }

    // The discriminator the document gives the step, as the serializer writes it, so a new kind needs nothing here.
    private static string Kind(SequenceStep step) =>
        JsonSerializer.SerializeToElement(step, DdtJsonContext.Default.SequenceStep).GetProperty("kind").GetString() ?? "";

    private static DeploymentArtifact Package(Guid runId, Guid stepId, ArtifactKind kind, Package package) => new()
    {
        DeploymentId = runId,
        StepId = stepId,
        Kind = kind,
        SourceId = package.Id,
        Name = package.Name,
        Sha256 = package.Sha256,
        SizeBytes = package.SizeBytes,
        ExpandedBytes = package.ExpandedBytes,
    };
}
