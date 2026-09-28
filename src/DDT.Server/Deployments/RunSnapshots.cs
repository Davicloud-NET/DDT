// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using DDT.Contracts;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Packages;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Packages;
using DDT.Server.Sequences;

namespace DDT.Server.Deployments;

// What is frozen when a run is created: a row per node of the sequence's tree and the files the steps download,
// resolved for the machine. Driver packages are matched to the machine's model now, so one uploaded later is not part of
// the run.
public static class RunSnapshots
{
    // The kinds that hold other nodes, as documents name them, so a run's rows tell its steps from their containers.
    private static readonly FrozenSet<string> s_containerKinds = typeof(SequenceStep)
        .GetCustomAttributes<JsonDerivedTypeAttribute>()
        .Where(kind => ((SequenceStep)RuntimeHelpers.GetUninitializedObject(kind.DerivedType)).IsContainer)
        .Select(kind => (string)kind.TypeDiscriminator!)
        .ToFrozenSet(StringComparer.Ordinal);

    // One row per node in pre-order, containers included, which for a flat sequence is a row per step as before. A node
    // whose phase depends on the path an IF takes starts with the first it may run in, and takes the phase the agent
    // reports when it runs.
    public static IReadOnlyList<DeploymentStep> Steps(Guid runId, SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        IReadOnlyList<SequenceStep> nodes = SequenceTree.Nodes(definition);
        IReadOnlyDictionary<Guid, NodePosition> positions = SequenceTree.Index(definition);
        IReadOnlyList<NodePhase> phases = SequenceChecks.NodePhases(definition);

        return
        [
            .. nodes.Select((node, order) => new DeploymentStep
            {
                DeploymentId = runId,
                StepId = node.Id,
                Index = order,
                Name = StoredText.Bound(node.Name, DeploymentLimits.MaxStepNameLength) ?? "",
                Kind = Kind(node),
                Phase = order < phases.Count && phases[order].Phases is [var first, ..] ? first : SequencePhase.WindowsPE,
                State = StepState.Pending,
                ParentId = positions[node.Id].ParentId,
                Depth = positions[node.Id].Depth,
            }),
        ];
    }

    // Whether a run's row is a group, an IF or a repeat rather than a step that does something.
    public static bool IsContainer(DeploymentStep row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return s_containerKinds.Contains(row.Kind);
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

        // Every branch, since any may run: an IF can apply a different image per model.
        foreach (SequenceStep step in SequenceTree.Nodes(definition))
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

    // The space a run needs on the disk it erases, the most any path through it needs (see SequenceSizes): its
    // partitions, and every file both downloaded and unpacked. A raw disk image is written as it downloads, so only the
    // disk it holds counts, and the seed after it.
    public static long RequiredBytes(SequenceDefinition definition, IReadOnlyList<DeploymentArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(artifacts);

        HashSet<Guid> rawSteps = [.. SequenceTree.Nodes(definition).OfType<WriteRawImageStep>().Select(step => step.Id)];
        Dictionary<Guid, long> files = artifacts
            .GroupBy(artifact => artifact.StepId)
            .ToDictionary(
                step => step.Key,
                step => step.Sum(artifact => rawSteps.Contains(artifact.StepId)
                    ? artifact.ExpandedBytes
                    : artifact.SizeBytes + artifact.ExpandedBytes));

        return SequenceSizes.RequiredBytes(definition, step => files.GetValueOrDefault(step.Id));
    }

    // Never a secret: the answer file and the join credentials are fetched while their step runs. An image a Write raw
    // disk image step names is a raw disk image, whatever the library holds by now. Values are those the run started with,
    // never an Account input's answer; pendingInputs what the machine asks before the run can start.
    public static AgentRun ForAgent(
        Deployment run,
        SequenceDefinition definition,
        IReadOnlyList<DeploymentArtifact> artifacts,
        string? computerName,
        IReadOnlyDictionary<string, string>? values = null,
        IReadOnlyList<AgentInput>? pendingInputs = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(artifacts);

        HashSet<Guid> rawSteps = [.. SequenceTree.Nodes(definition).OfType<WriteRawImageStep>().Select(step => step.Id)];

        return new AgentRun(
            run.Id,
            run.State,
            run.Title,
            definition,
            [
                // Two branches can apply the same image, which the agent needs once.
                .. artifacts
                    .Where(a => a.Kind == ArtifactKind.Image)
                    .DistinctBy(a => (a.SourceId, rawSteps.Contains(a.StepId)))
                    .Select(a => rawSteps.Contains(a.StepId)
                        ? new AgentRunImage(a.SourceId, a.Name, a.Sha256, a.SizeBytes, 0, a.ExpandedBytes, ImageKind.RawDisk, a.BootCapability, a.SignedUnder)
                        : new AgentRunImage(a.SourceId, a.Name, a.Sha256, a.SizeBytes, a.WimIndex ?? 1, a.ExpandedBytes)),
            ],
            [.. artifacts.Where(a => a.Kind != ArtifactKind.Image).Select(a => new AgentRunPackage(a.StepId, a.Name, a.Sha256, a.SizeBytes))],
            run.DiskNumber,
            computerName,
            run.AllowSecureBootMismatch,
            values,
            pendingInputs is { Count: > 0 } ? pendingInputs : null);
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
