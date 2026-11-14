// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Packages;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Packages;
using DDT.Server.Sequences;
using Xunit;

namespace DDT.Server.Tests.WindowsPackages;

// What a run of a Service packages step downloads: its list, then the updates marked for its image's build.
public sealed class RunWindowsPackagesTests
{
    private static readonly Guid s_runId = Guid.NewGuid();

    private static readonly Image s_image = new()
    {
        Id = Guid.NewGuid(),
        Name = "Windows 11 Pro 25H2",
        Kind = ImageKind.Wim,
        Sha256 = new string('a', 64),
        Version = "10.0.26200.6584",
        Architecture = "x64",
    };

    private readonly Package _german = Windows("de-DE language pack", WindowsPackageContent.Language, null, "x64", null);
    private readonly Package _november = Windows("2026-11 update", WindowsPackageContent.Update, "5132061,5043080", "x64", "26200");
    private readonly Package _july = Windows("2026-07 update", WindowsPackageContent.Update, "5040000", null, "26100,26200");
    private readonly Package _arm = Windows("2026-11 update for Arm", WindowsPackageContent.Update, "5132062", "arm64", "26200");
    private readonly Package _older = Windows("24H2 update", WindowsPackageContent.Update, "5132063", "x64", "26100");
    private readonly Package _unmarked = Windows("Unmarked update", WindowsPackageContent.Update, "5132064", "x64", null);

    [Fact]
    public void TheListComesFirstThenTheMatchedUpdatesLowestKbFirstAndNoneTwice()
    {
        ServicePackagesStep step = Step(true, _german.Id, _november.Id);

        IEnumerable<DeploymentArtifact> artifacts = RunWindowsPackages.Artifacts(s_runId, step, Definition(step), References());

        Assert.Equal(["de-DE language pack", "2026-11 update", "2026-07 update"], artifacts.Select(artifact => artifact.Name));
        Assert.All(artifacts, artifact => Assert.Equal((ArtifactKind.WindowsPackage, step.Id), (artifact.Kind, artifact.StepId)));
    }

    [Fact]
    public void WithoutMatchingUpdatesOnlyTheListCounts()
    {
        ServicePackagesStep step = Step(false, _german.Id);

        Assert.Equal([_german.Id], RunWindowsPackages.Artifacts(s_runId, step, Definition(step), References()).Select(artifact => artifact.SourceId));
    }

    [Fact]
    public void TheAgentLearnsWhichPackagesWereMatched()
    {
        ServicePackagesStep step = Step(true, _german.Id);
        SequenceDefinition definition = Definition(step);
        DeploymentArtifact[] artifacts = [.. RunSnapshots.Artifacts(s_runId, definition, References(), new Machine { SmbiosUuid = "uuid", PrimaryMac = "020000000001" })];
        Deployment run = new() { Id = s_runId, Title = "Install Windows", State = DeploymentState.Assigned };

        AgentRun handed = RunSnapshots.ForAgent(run, definition, artifacts, null, null);

        Assert.Equal(
            [("de-DE language pack", false), ("2026-07 update", true), ("2026-11 update", true)],
            handed.Packages.Select(package => (package.Name, package.Matched)));
    }

    private static ServicePackagesStep Step(bool matching, params Guid[] packages) =>
        new() { Id = Guid.NewGuid(), Name = "Service packages", Packages = packages, MatchingUpdates = matching };

    private static SequenceDefinition Definition(ServicePackagesStep step) => new(
        SequenceDefinition.CurrentVersion,
        [new PartitionStep { Id = Guid.NewGuid(), Name = "Partition" }, new ApplyImageStep { Id = Guid.NewGuid(), Name = "Apply", ImageId = s_image.Id }, step]);

    private static Package Windows(string name, WindowsPackageContent content, string? kbs, string? architecture, string? builds) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Kind = PackageKind.WindowsPackage,
        Sha256 = new string('b', 64),
        Content = content,
        KbNumbers = kbs,
        Architecture = architecture,
        Builds = builds,
    };

    private SequenceReferences References() => new(
        new Dictionary<Guid, Image> { [s_image.Id] = s_image },
        new[] { _german, _november, _july, _arm, _older, _unmarked }.ToDictionary(package => package.Id),
        false,
        false);
}
