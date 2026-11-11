// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using DDT.Server.Packages;
using Xunit;
using static DDT.Server.Tests.ApplicationRequests;
using static DDT.Server.Tests.Applications.ApplicationRuns;

namespace DDT.Server.Tests.Applications;

// What a run freezes of the applications its steps name when it is created: a plan per step in install order, and the
// files it downloads, which the library keeps while the run is on.
public sealed class RunApplicationPlanTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task ABundleAndDependenciesArePlannedInInstallOrderWithTheirFiles()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView runtime = await administrator.WithFilesAsync("Runtime");
        ApplicationView app = await administrator.WithFilesAsync("App", runtime.Id);
        ApplicationView tool = await administrator.CreatedApplicationAsync(Command(Unique("Tool")));
        ApplicationView last = await administrator.WithFilesAsync("Last");
        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle(Unique("Desk"), tool.Id, app.Id));
        InstallApplicationsStep install = Install(null, Of(bundle), Of(last));
        SequenceView sequence = await application.SequenceAsync(null, install);

        (DeployingMachine machine, AgentRun run) = await application.AssignedAsync(sequence);
        using DeployingMachine held = machine;

        IReadOnlyList<AgentApplication> plan = Assert.IsAssignableFrom<IReadOnlyList<AgentApplication>>(await application.PlanAsync(run.Id, install.Id));
        Assert.Equal([tool.Id, runtime.Id, app.Id, last.Id], plan.Select(planned => planned.Id));

        Package files = await application.FilesOfAsync(runtime);
        AgentApplication first = plan[1];
        Assert.Equal(
            (runtime.Name, ApplicationKind.Command, "setup.cmd /quiet", 60, false),
            (first.Name, first.Kind, first.Command, first.TimeoutMinutes, first.ContinueOnFailure));
        Assert.Equal([0], first.SuccessExitCodes);
        Assert.Equal([3010], first.RestartExitCodes);
        Assert.Equal(new AgentApplicationFiles(files.Sha256, files.SizeBytes, files.ExpandedBytes, files.Name), first.Files);

        List<DeploymentArtifact> artifacts = await application.ApplicationArtifactsAsync(run.Id);
        Assert.Equal(
            new[] { PackageOf(runtime), PackageOf(app), PackageOf(last) }.Order(),
            artifacts.Select(artifact => artifact.SourceId).Order());
        Assert.All(artifacts, artifact => Assert.Equal(install.Id, artifact.StepId));
        DeploymentArtifact frozen = Assert.Single(artifacts, artifact => artifact.SourceId == PackageOf(runtime));
        Assert.Equal(
            (runtime.Name, files.Sha256, files.SizeBytes, files.ExpandedBytes),
            (frozen.Name, frozen.Sha256, frozen.SizeBytes, frozen.ExpandedBytes));

        // The agent counts them like packages.
        Assert.Equal(
            artifacts.Select(artifact => artifact.Sha256).Order(),
            run.Packages.Where(package => package.StepId == install.Id).Select(package => package.Sha256).Order());
    }

    [Fact]
    public async Task AnApplicationWithoutFilesIsPlannedWithoutAnything()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView winget = await administrator.CreatedApplicationAsync(
            Command(Unique("Terminal")) with { Kind = ApplicationKind.Winget, Command = null, WingetId = "Microsoft.WindowsTerminal", WingetVersion = "1.21" });
        InstallApplicationsStep install = Install(null, Of(winget));
        SequenceView sequence = await application.SequenceAsync(
            null,
            new WriteUnattendStep { Id = Guid.NewGuid(), Name = "Answer file" },
            install);

        (DeployingMachine machine, AgentRun run) = await application.AssignedAsync(sequence);
        using DeployingMachine held = machine;

        AgentApplication planned = Assert.Single(await application.PlanAsync(run.Id, install.Id) ?? []);
        Assert.Equal(
            (winget.Id, ApplicationKind.Winget, (string?)null, "Microsoft.WindowsTerminal", "1.21", (AgentApplicationFiles?)null),
            (planned.Id, planned.Kind, planned.Command, planned.WingetId, planned.WingetVersion, planned.Files));
        Assert.Empty(await application.ApplicationArtifactsAsync(run.Id));
        Assert.Empty(run.Packages);
    }

    [Fact]
    public async Task TwoStepsThatInstallTheSameApplicationEachHaveIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView app = await administrator.WithFilesAsync("Twice");
        InstallApplicationsStep first = Install(null, Of(app));
        InstallApplicationsStep second = Install(null, Of(app)) with { Name = "Install it again" };
        SequenceView sequence = await application.SequenceAsync(null, first, second);

        (DeployingMachine machine, AgentRun run) = await application.AssignedAsync(sequence);
        using DeployingMachine held = machine;

        Assert.Equal(app.Id, Assert.Single(await application.PlanAsync(run.Id, first.Id) ?? []).Id);
        Assert.Equal(app.Id, Assert.Single(await application.PlanAsync(run.Id, second.Id) ?? []).Id);
        Assert.Equal([first.Id, second.Id], (await application.ApplicationArtifactsAsync(run.Id)).Select(artifact => artifact.StepId));
    }

    [Fact]
    public async Task AStepThatOnlyReadsAValueStartsWithAnEmptyPlan()
    {
        InstallApplicationsStep install = Install("Extras");
        SequenceView sequence = await application.SequenceAsync(null, install);

        (DeployingMachine machine, AgentRun run) = await application.AssignedAsync(sequence);
        using DeployingMachine held = machine;

        Assert.Equal([], await application.PlanAsync(run.Id, install.Id));
    }

    [Fact]
    public async Task ALibraryEditAfterTheRunWasCreatedDoesNotChangeItsPlan()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView app = await administrator.WithFilesAsync("Frozen");
        InstallApplicationsStep install = Install(null, Of(app));
        SequenceView sequence = await application.SequenceAsync(null, install);
        (DeployingMachine machine, AgentRun run) = await application.AssignedAsync(sequence);
        using DeployingMachine held = machine;

        (await administrator.SaveApplicationAsync(app.Id, Keep(app) with
        {
            Name = Unique("Thawed"),
            Command = "other.cmd",
            SuccessExitCodes = [0, 1],
            Detection = new FileDetection(@"C:\Tools\tool.exe"),
            TimeoutMinutes = 5,
        })).EnsureSuccessStatusCode();

        AgentApplication planned = Assert.Single(await application.PlanAsync(run.Id, install.Id) ?? []);
        Assert.Equal((app.Name, "setup.cmd /quiet", 60, (ApplicationDetection?)null), (planned.Name, planned.Command, planned.TimeoutMinutes, planned.Detection));
        Assert.Equal([0], planned.SuccessExitCodes);
    }

    // A machine waiting to download them would fail at the step, long after its disk was erased.
    [Fact]
    public async Task FilesARunFrozeCannotBeReplacedOrDeletedWhileItIsOn()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView app = await administrator.WithFilesAsync("Kept");
        SequenceView sequence = await application.SequenceAsync(null, Install(null, Of(app)));
        (DeployingMachine machine, _) = await application.AssignedAsync(sequence);
        using DeployingMachine held = machine;
        ApplicationUpload replacement = await administrator.UploadedApplicationAsync(RandomNumberGenerator.GetBytes(256), "new.exe");

        HttpResponseMessage replaced = await administrator.SaveApplicationAsync(app.Id, Keep(app) with { PackageId = replacement.Files.PackageId });

        Assert.Equal((HttpStatusCode.Conflict, "application.filesInUse"), (replaced.StatusCode, await CodeAsync(replaced)));

        // The sequence no longer names it, but the run still downloads its files.
        (await administrator.SaveSequenceAsync(sequence, SequenceRequests.Minimal((await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096))).Id))).EnsureSuccessStatusCode();
        HttpResponseMessage deleted = await administrator.DeleteAsync($"{ApplicationsPath}/{app.Id:D}");

        Assert.Equal((HttpStatusCode.Conflict, "application.filesInUse"), (deleted.StatusCode, await CodeAsync(deleted)));

        (await administrator.EndCurrentAsync(machine.Id)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{ApplicationsPath}/{app.Id:D}")).StatusCode);
    }
}
