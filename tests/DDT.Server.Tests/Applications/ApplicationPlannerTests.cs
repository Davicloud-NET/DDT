// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Deployments;
using DDT.Contracts.Messages;
using DDT.Contracts.Packages;
using DDT.Contracts.Sequences;
using DDT.Core.Applications;
using DDT.Server.Applications;
using DDT.Server.Deployments;
using DDT.Server.Packages;
using Xunit;

namespace DDT.Server.Tests.Applications;

// The applications a step installs, worked out from a library that may have changed since the sequence was saved.
public sealed class ApplicationPlannerTests
{
    private const string Step = "Install the tools";

    private static Application App(string name, Guid? packageId = null, params Guid[] dependsOn) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        NormalizedName = ApplicationNames.Normalize(name) ?? "",
        PackageId = packageId,
        Command = "setup.cmd",
        DependsOn = Ids(dependsOn),
        TimeoutMinutes = 60,
    };

    private static Package Files(long size) => new()
    {
        Id = Guid.NewGuid(),
        Name = "files.zip",
        Kind = PackageKind.Application,
        Sha256 = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()).PadRight(64, '0'),
        SizeBytes = size,
        ExpandedBytes = size * 2,
    };

    private static ApplicationLibrary Library(IEnumerable<Application> applications, IEnumerable<ApplicationBundle>? bundles = null, IEnumerable<Package>? files = null) =>
        new(applications.ToDictionary(a => a.Id), (bundles ?? []).ToDictionary(b => b.Id), (files ?? []).ToDictionary(p => p.Id));

    private static string Ids(params Guid[] ids) => $"[{string.Join(',', ids.Select(id => $"\"{id:D}\""))}]";

    private static ApplicationReference Of(Application application) => new(application.Id, ApplicationReferenceKind.Application);

    [Fact]
    public void ABundleStandsForItsMembersAndEachComesAfterItsDependenciesOnce()
    {
        Application runtime = App("Runtime");
        Application app = App("App", null, runtime.Id);
        Application tool = App("Tool", null, runtime.Id);
        ApplicationBundle bundle = new() { Id = Guid.NewGuid(), Name = "Desk", NormalizedName = "DESK", Members = Ids(app.Id, runtime.Id) };

        ApplicationPlan plan = ApplicationPlanner.Resolve(
            Step,
            [new ApplicationReference(bundle.Id, ApplicationReferenceKind.Bundle), Of(tool), Of(app)],
            Library([runtime, app, tool], [bundle]));

        Assert.Null(plan.Problem);
        Assert.Equal([runtime.Id, app.Id, tool.Id], plan.Order.Select(a => a.Id));
    }

    [Fact]
    public void ACircleIsAProblemThatNamesItsPath()
    {
        Guid firstId = Guid.NewGuid();
        Application second = App("Second", null, firstId);
        Application first = App("First", null, second.Id);
        first.Id = firstId;

        ApplicationPlan plan = ApplicationPlanner.Resolve(Step, [Of(first)], Library([first, second]));

        Assert.Empty(plan.Order);
        Assert.Equal(
            ServerMessages.DeploymentApplicationCycle.With("step", Step, "path", "First > Second > First").Text,
            plan.Problem?.Text);
    }

    [Fact]
    public void ADependencyNoLongerInTheLibraryNamesTheApplicationThatNeedsIt()
    {
        Application app = App("Needy", null, Guid.NewGuid());

        ApplicationPlan plan = ApplicationPlanner.Resolve(Step, [Of(app)], Library([app]));

        Assert.Equal(
            $"Needy, which {Step} installs, depends on an application that is no longer in the library.",
            plan.Problem?.Text);
    }

    [Fact]
    public void AnEntryNoLongerInTheLibraryIsAProblem()
    {
        ApplicationPlan gone = ApplicationPlanner.Resolve(Step, [new ApplicationReference(Guid.NewGuid(), ApplicationReferenceKind.Bundle)], Library([]));

        Assert.Equal(ServerMessages.SequenceStepApplicationGone.Code, gone.Problem?.Code);
    }

    // The fixed list is worked out again when the run is created, which then refuses it.
    [Fact]
    public void FilesNoLongerInTheLibraryKeepTheRunFromBeingCreated()
    {
        Application lost = App("Lost", Guid.NewGuid());
        InstallApplicationsStep install = new() { Id = Guid.NewGuid(), Name = Step, Applications = [Of(lost)] };

        (IReadOnlyList<DeploymentApplicationPlan> plans, ServerMessage? problem) = RunApplicationPlans.Fixed(
            Guid.NewGuid(),
            new SequenceDefinition(SequenceDefinition.CurrentVersion, [install]),
            Library([lost]));

        Assert.Empty(plans);
        Assert.Equal($"The files of Lost, which {Step} installs, are no longer in the library.", problem?.Text);
    }

    // Applications that share files download them once per step.
    [Fact]
    public void ApplicationsThatShareFilesFreezeThemOnce()
    {
        Package files = Files(100);
        Application first = App("First", files.Id);
        Application second = App("Second", files.Id);
        InstallApplicationsStep install = new() { Id = Guid.NewGuid(), Name = Step, Applications = [Of(first), Of(second)] };

        DeploymentArtifact artifact = Assert.Single(RunApplicationPlans.FixedArtifacts(Guid.NewGuid(), install, Library([first, second], null, [files])));

        Assert.Equal(
            (ArtifactKind.Application, files.Id, install.Id, "First", files.Sha256, 100L, 200L),
            (artifact.Kind, artifact.SourceId, artifact.StepId, artifact.Name, artifact.Sha256, artifact.SizeBytes, artifact.ExpandedBytes));
    }
}
