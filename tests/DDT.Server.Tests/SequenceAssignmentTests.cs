// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Packages;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class SequenceAssignmentTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private async Task<HttpResponseMessage> AssignAsync(Guid machineId, Guid sequenceId, string? computerName = null) =>
        await (await application.AdministratorAsync()).AssignAsync(machineId, sequenceId, computerName);

    private async Task<HttpResponseMessage> EndCurrentAsync(Guid machineId) =>
        await (await application.AdministratorAsync()).EndCurrentAsync(machineId);

    private async Task<SequenceView> SequenceAsync(SequenceDefinition definition) =>
        await (await application.AdministratorAsync()).CreatedSequenceAsync(definition);

    private Task<Image> ImageAsync(string? architecture = "x64") => application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096), architecture);

    private Task<List<string>> AuditAsync(Guid subjectId)
    {
        string subject = subjectId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject)
            .OrderBy(e => e.Id)
            .Select(e => e.Action + " " + e.Detail)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AssigningToAMachineWaitingAtThePromptAuthorizesIt()
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application, [DeployingMachine.Disk(0)]);
        Image image = await ImageAsync();
        SequenceView sequence = await SequenceAsync(SequenceRequests.Minimal(image.Id));

        MachineSummary summary = await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, sequence.Id, "PC-0001"));

        Assert.Equal(MachineState.Approved, summary.State);
        Assert.True(summary.EverApproved);
        Assert.Equal("PC-0001", summary.AssignedName);
        Assert.Equal("Disk 0: Msft Virtual Disk, 64 GB, SCSI", summary.Disks);
        DeploymentSummary run = Assert.IsType<DeploymentSummary>(summary.Deployment);
        Assert.Equal(DeploymentState.Assigned, run.State);
        Assert.Equal(DeploymentSource.Web, run.Source);
        Assert.Equal(sequence.Id, run.SequenceId);
        Assert.Equal(sequence.Name, run.Title);
        Assert.Equal(2, run.StepCount);
        Assert.Null(run.StepIndex);
        Assert.Null(run.StartedUtc);
        Assert.StartsWith("administrator-", run.RequestedBy, StringComparison.Ordinal);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Approved, next.State);
        Assert.False(next.CanPickSequence);
        Assert.False(next.CanPickImage);
        Assert.Null(next.Deployment);
        Assert.Equal("PC-0001", next.AssignedName);

        AgentRun handed = Assert.IsType<AgentRun>(next.Run);
        Assert.Equal(run.Id, handed.Id);
        Assert.Equal(DeploymentState.Assigned, handed.State);
        Assert.Equal(sequence.Name, handed.SequenceName);
        Assert.Equal(image.Sha256, Assert.Single(handed.Images).Sha256);
        Assert.Equal("PC-0001", handed.ComputerName);

        Assert.Equal(
            [$"{AuditActions.DeploymentAssigned} {sequence.Name}, revision 1, to machine {machine.Id:D}."],
            await AuditAsync(run.Id));
        Assert.Contains($"{AuditActions.MachineApproved} Was Pending. Approved by assigning {sequence.Name}.", await AuditAsync(machine.Id));
    }

    // The run keeps the definition, the steps and the files as they were when it was assigned.
    [Fact]
    public async Task TheRunKeepsWhatItWasGivenWhenTheSequenceChanges()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await ImageAsync();
        SequenceView sequence = await SequenceAsync(SequenceRequests.Minimal(image.Id));
        DeploymentSummary run = await administrator.AssignedAsync(machine.Id, sequence.Id);

        SequenceDefinition edited = SequenceRequests.Definition([.. sequence.Definition.Steps, .. SequenceRequests.ScriptOnly().Steps]);
        (await administrator.SaveSequenceAsync(sequence, edited, name: $"Renamed {Guid.NewGuid():N}")).EnsureSuccessStatusCode();

        // What runs is what the agent is handed, and that is the frozen sequence too.
        AgentRun handed = (await machine.NextAsync()).Run!;

        Assert.Equal(sequence.Name, handed.SequenceName);
        Assert.Equal(SequenceRequests.Json(sequence.Definition), SequenceRequests.Json(handed.Sequence));
        Assert.Equal(image.Sha256, Assert.Single(handed.Images).Sha256);

        DeploymentView view = await administrator.RunAsync(run.Id);

        Assert.Equal(sequence.Name, view.Summary.Title);
        Assert.Equal(1, view.SequenceRevision);
        Assert.Equal(machine.Id, view.MachineId);
        Assert.Null(view.RuleId);
        Assert.Equal(SequenceRequests.Json(sequence.Definition), SequenceRequests.Json(view.Definition!));
        Assert.Equal(
            [(sequence.Definition.Steps[0].Id, 0, "Partition", "partition"), (sequence.Definition.Steps[1].Id, 1, "Apply", "applyImage")],
            view.Steps.Select(s => (s.StepId, s.Index, s.Name, s.Kind)));
        Assert.All(view.Steps, step =>
        {
            Assert.Equal(StepState.Pending, step.State);
            Assert.Equal(SequencePhase.WindowsPE, step.Phase);
            Assert.Null(step.StartedUtc);
        });

        DeploymentArtifactView artifact = Assert.Single(view.Artifacts);
        Assert.Equal(new DeploymentArtifactView(sequence.Definition.Steps[1].Id, ArtifactKind.Image, image.Id, image.Name, image.Sha256, image.SizeBytes), artifact);

        // The image cannot go while the run may still download it.
        HttpResponseMessage delete = await administrator.DeleteAsync($"/api/images/{image.Id}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task DriverPackagesAreChosenForTheMachinesModel()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await ImageAsync();
        Package matching = await application.SeedPackageAsync(PackageKind.Drivers, new HardwareModel("Microsoft Corporation", "Virtual*"));
        Package other = await application.SeedPackageAsync(PackageKind.Drivers, new HardwareModel(null, "Latitude 5440"));
        Package files = await application.SeedPackageAsync(PackageKind.Files);
        InjectDriversStep drivers = new() { Id = Guid.NewGuid(), Name = "Drivers" };
        RunScriptStep script = new()
        {
            Id = Guid.NewGuid(),
            Name = "Script",
            Phase = SequencePhase.WindowsPE,
            Interpreter = ScriptInterpreter.Cmd,
            Script = "setup.cmd",
            PackageId = files.Id,
        };
        SequenceView sequence = await SequenceAsync(SequenceRequests.Definition([.. SequenceRequests.Minimal(image.Id).Steps, drivers, script]));

        DeploymentView view = await administrator.RunAsync((await administrator.AssignedAsync(machine.Id, sequence.Id)).Id);

        Assert.Equal(
            [
                (ArtifactKind.Image, image.Id),
                (ArtifactKind.Drivers, matching.Id),
                (ArtifactKind.Files, files.Id),
            ],
            view.Artifacts.Select(a => (a.Kind, a.SourceId)));
        Assert.Equal(drivers.Id, view.Artifacts[1].StepId);
        Assert.Equal(script.Id, view.Artifacts[2].StepId);
        Assert.DoesNotContain(view.Artifacts, a => a.SourceId == other.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.DeleteAsync($"{PackageRequests.Packages}/{matching.Id}")).StatusCode);
    }

    [Fact]
    public async Task AssigningToAMachineSeenLongAgoLeavesItWaiting()
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));

        MachineSummary summary = await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, sequence.Id));

        Assert.Equal(MachineState.Pending, summary.State);
        Assert.Equal(DeploymentState.Assigned, summary.Deployment?.State);

        // Whoever holds the tokens of a registration seen that long ago may not be the machine at the prompt.
        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Pending, next.State);
        Assert.Null(next.Run);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await machine.Agent.LogAsync(machine.Id, next.Token, new AgentLogBatch([new AgentLogLine(DateTimeOffset.UtcNow, AgentLogLevel.Information, "x")]))).StatusCode);
    }

    [Fact]
    public async Task AFailedOrDoneMachineIsApprovedAgainByAnAssignment()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        Guid first = (await administrator.AssignedAsync(machine.Id, sequence.Id)).Id;
        await application.MoveRunAsync(machine.Id, DeploymentState.Failed, "The script failed.");

        Assert.Equal(MachineState.Failed, (await machine.NextAsync()).State);

        MachineSummary again = await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, sequence.Id));

        Assert.Equal(MachineState.Approved, again.State);
        Assert.NotEqual(first, again.Deployment!.Id);
    }

    // Done refuses every token, and the tokens the machine held while it ran must stay dead once an assignment
    // makes the machine Approved again: the agent that held them rebooted.
    [Fact]
    public async Task AnAssignmentAfterDoneKeepsTheFinishedRunsTokensDead()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        await administrator.AssignedAsync(machine.Id, sequence.Id);
        await machine.NextAsync();
        await application.MoveRunAsync(machine.Id, DeploymentState.Running);
        await application.MoveRunAsync(machine.Id, DeploymentState.Done);

        string sessionToken = machine.Token;
        string resumeToken = machine.ResumeToken;

        Assert.Equal(MachineState.Approved, (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, sequence.Id))).State);
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.NextAsync(machine.Id, sessionToken)).StatusCode);

        AgentRegistrationResult resumed = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration with { ResumeToken = resumeToken }));

        Assert.Equal(MachineState.Pending, resumed.State);
    }

    [Fact]
    public async Task RefusesWhatTheMachineCannotRun()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine twoDisks = await DeployingMachine.ApprovedAsync(
            application,
            administrator,
            [DeployingMachine.Disk(0), DeployingMachine.Disk(1, "Samsung SSD 990 PRO")]);
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView erasing = await SequenceAsync(SequenceRequests.Minimal((await ImageAsync()).Id));
        SequenceView arm = await SequenceAsync(SequenceRequests.Minimal((await ImageAsync("arm64")).Id));
        SequenceView script = await SequenceAsync(SequenceRequests.ScriptOnly());

        HttpResponseMessage disks = await AssignAsync(twoDisks.Id, erasing.Id);
        Assert.Equal(HttpStatusCode.Conflict, disks.StatusCode);
        Assert.Equal(
            $"{erasing.Name} erases a disk, and this machine has more than one. Sign in at it and choose the disk there.",
            await TestDatabase.TitleAsync(disks));

        HttpResponseMessage problem = await AssignAsync(machine.Id, arm.Id);
        Assert.Equal(HttpStatusCode.Conflict, problem.StatusCode);
        Assert.Equal($"{arm.Name} has a problem, so it cannot run. Fix it on the sequence's page first.", await TestDatabase.TitleAsync(problem));

        Assert.Equal(HttpStatusCode.NotFound, (await AssignAsync(machine.Id, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AssignAsync(Guid.NewGuid(), script.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(machine.Id, script.Id, "PC_0001")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(machine.Id, script.Id, "1234")).StatusCode);

        // A sequence that erases nothing needs no disk chosen.
        (await AssignAsync(twoDisks.Id, script.Id)).EnsureSuccessStatusCode();
        (await AssignAsync(machine.Id, script.Id)).EnsureSuccessStatusCode();

        HttpResponseMessage twice = await AssignAsync(machine.Id, script.Id);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("This machine already has a run. Cancel it before assigning another sequence.", await TestDatabase.TitleAsync(twice));

        Assert.Null((await application.MachineAsync(machine.Id)).AssignedName);
    }

    [Fact]
    public async Task RefusesARejectedOrRunningMachine()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine running = await DeployingMachine.ApprovedAsync(application, administrator);
        using DeployingMachine rejected = await DeployingMachine.RegisterAsync(application);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        await administrator.AssignedAsync(running.Id, sequence.Id);
        await application.MoveRunAsync(running.Id, DeploymentState.Running);
        (await administrator.PostAsync($"/api/machines/{rejected.Id}/reject")).EnsureSuccessStatusCode();

        HttpResponseMessage busy = await AssignAsync(running.Id, sequence.Id);
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        Assert.Equal(
            "The machine is running a task sequence. Stop that run before assigning another sequence.",
            await TestDatabase.TitleAsync(busy));
        Assert.Equal(
            "The machine was rejected, so it cannot be given a sequence. Assign the sequence to another machine.",
            await TestDatabase.TitleAsync(await AssignAsync(rejected.Id, sequence.Id)));
    }

    [Fact]
    public async Task CancellingAnAssignmentLeavesTheMachineAsItWas()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        Guid run = (await administrator.AssignedAsync(machine.Id, sequence.Id)).Id;

        MachineSummary cancelled = await RegisteredMachine.ReadAsync<MachineSummary>(await EndCurrentAsync(machine.Id));

        Assert.Equal(MachineState.Approved, cancelled.State);
        Assert.Equal(run, cancelled.Deployment?.Id);
        Assert.Equal(DeploymentState.Cancelled, cancelled.Deployment?.State);
        Assert.NotNull(cancelled.Deployment?.FinishedUtc);
        Assert.Null((await machine.NextAsync()).Run);

        HttpResponseMessage again = await EndCurrentAsync(machine.Id);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.StartsWith("This machine has no run that is assigned or running.", await TestDatabase.TitleAsync(again), StringComparison.Ordinal);
        Assert.Contains(await AuditAsync(run), a => a.StartsWith(AuditActions.DeploymentCancelled + " ", StringComparison.Ordinal));

        // Nothing blocks a new assignment.
        (await AssignAsync(machine.Id, sequence.Id)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task StoppingARunningRunFailsItAndCutsOffTheAgent()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        Guid run = (await administrator.AssignedAsync(machine.Id, sequence.Id)).Id;
        await machine.NextAsync();
        await application.MoveRunAsync(machine.Id, DeploymentState.Running);

        MachineSummary stopped = await RegisteredMachine.ReadAsync<MachineSummary>(await EndCurrentAsync(machine.Id));

        Assert.Equal(MachineState.Failed, stopped.State);
        Assert.Equal(DeploymentState.Failed, stopped.Deployment?.State);
        Assert.StartsWith("Stopped by administrator-", stopped.Deployment?.Error, StringComparison.Ordinal);
        Assert.Contains(
            $"{AuditActions.DeploymentFailed} {sequence.Name} on machine {machine.Id:D}. {stopped.Deployment?.Error}",
            await AuditAsync(run));
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.NextAsync(machine.Id, machine.Token)).StatusCode);

        // Its resume token died with the generation, so the agent starts over.
        AgentRegistrationResult again = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration with { ResumeToken = machine.ResumeToken }));

        Assert.Equal(MachineState.Pending, again.State);
    }

    [Fact]
    public async Task RejectingAMachineEndsItsRun()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine assigned = await DeployingMachine.ApprovedAsync(application, administrator);
        using DeployingMachine running = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        Guid assignment = (await administrator.AssignedAsync(assigned.Id, sequence.Id)).Id;
        Guid run = (await administrator.AssignedAsync(running.Id, sequence.Id)).Id;
        await application.MoveRunAsync(running.Id, DeploymentState.Running);

        MachineSummary cancelled = await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.PostAsync($"/api/machines/{assigned.Id}/reject"));
        MachineSummary failed = await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.PostAsync($"/api/machines/{running.Id}/reject"));

        Assert.Equal(MachineState.Rejected, cancelled.State);
        Assert.Equal(DeploymentState.Cancelled, cancelled.Deployment?.State);
        Assert.Equal(MachineState.Rejected, failed.State);
        Assert.Equal(DeploymentState.Failed, failed.Deployment?.State);
        Assert.StartsWith("Rejected by administrator-", failed.Deployment?.Error, StringComparison.Ordinal);

        // The administrator who assigned both runs also rejected both machines.
        string administratorName = cancelled.Deployment!.RequestedBy!;
        Assert.Contains(
            $"{AuditActions.DeploymentCancelled} {sequence.Name} on machine {assigned.Id:D}. The machine was rejected by {administratorName}.",
            await AuditAsync(assignment));
        Assert.Contains(
            $"{AuditActions.DeploymentFailed} {sequence.Name} on machine {running.Id:D}. Rejected by {administratorName}.",
            await AuditAsync(run));
        Assert.Null((await application.MachineAsync(running.Id)).ActiveDeploymentId);

        // Rejected stays final: a second rejection is refused.
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.PostAsync($"/api/machines/{running.Id}/reject")).StatusCode);
    }

    [Fact]
    public async Task AMachineWhoseRunFailedCanBeRejected()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        Guid run = (await administrator.AssignedAsync(machine.Id, sequence.Id)).Id;
        await application.MoveRunAsync(machine.Id, DeploymentState.Running);
        await application.MoveRunAsync(machine.Id, DeploymentState.Failed, "The script failed.");

        MachineSummary rejected = await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.PostAsync($"/api/machines/{machine.Id}/reject"));

        Assert.Equal(MachineState.Rejected, rejected.State);
        Assert.Equal(run, rejected.Deployment?.Id);
        Assert.Equal(DeploymentState.Failed, rejected.Deployment?.State);
        Assert.Equal("The script failed.", rejected.Deployment?.Error);
    }

    // Both leave the machine Approved, so only the concurrency token on ActiveDeploymentId stops the second one.
    [Fact]
    public async Task TwoAssignmentsRacingForOneMachineLeaveOneRun()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        using IServiceScope first = application.Services.CreateScope();
        using IServiceScope second = application.Services.CreateScope();
        DdtDbContext firstDatabase = first.ServiceProvider.GetRequiredService<DdtDbContext>();
        DdtDbContext secondDatabase = second.ServiceProvider.GetRequiredService<DdtDbContext>();
        Machine firstMachine = await firstDatabase.Machines.SingleAsync(m => m.Id == machine.Id, cancellationToken);
        Machine secondMachine = await secondDatabase.Machines.SingleAsync(m => m.Id == machine.Id, cancellationToken);

        DeploymentDecision firstDecision = await first.ServiceProvider.GetRequiredService<DeploymentService>()
            .AssignAsync(firstMachine, new AssignSequenceRequest(sequence.Id, null), null, "first", null, cancellationToken);
        DeploymentDecision secondDecision = await second.ServiceProvider.GetRequiredService<DeploymentService>()
            .AssignAsync(secondMachine, new AssignSequenceRequest(sequence.Id, null), null, "second", null, cancellationToken);

        Assert.Equal(DeploymentOutcome.Accepted, firstDecision.Outcome);
        Assert.Equal(DeploymentOutcome.Accepted, secondDecision.Outcome);

        await firstDatabase.SaveChangesAsync(cancellationToken);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDatabase.SaveChangesAsync(cancellationToken));

        Assert.Equal(firstDecision.Deployment!.Id, (await application.MachineAsync(machine.Id)).ActiveDeploymentId);
        Assert.Equal(
            1,
            await application.QueryAsync(database => database.Deployments.CountAsync(d => d.MachineId == machine.Id, cancellationToken)));
    }

    // Holding the library lock stands in for an image deletion that runs between the lookup and the save.
    [Fact]
    public async Task AnAssignmentWaitsForTheImageLibrary()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, await application.AdministratorAsync());
        Image image = await ImageAsync();
        SequenceView sequence = await SequenceAsync(SequenceRequests.Minimal(image.Id));
        ImageStore store = application.Services.GetRequiredService<ImageStore>();
        Task<HttpResponseMessage> assigning;

        await store.LibraryLock.WaitAsync(cancellationToken);

        try
        {
            assigning = AssignAsync(machine.Id, sequence.Id);
            await Task.WhenAny(assigning, Task.Delay(TimeSpan.FromSeconds(1), cancellationToken));
            Assert.False(assigning.IsCompleted);

            await application.QueryAsync(database => database.Images.Where(i => i.Id == image.Id).ExecuteDeleteAsync(cancellationToken));
        }
        finally
        {
            store.LibraryLock.Release();
        }

        HttpResponseMessage response = await assigning;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal($"{sequence.Name} has a problem, so it cannot run. Fix it on the sequence's page first.", await TestDatabase.TitleAsync(response));
        Assert.Null((await application.MachineAsync(machine.Id)).ActiveDeploymentId);
    }

    [Fact]
    public async Task OnlyOperatorsAssignAndEveryViewerSeesTheRuns()
    {
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.AssignAsync(machine.Id, sequence.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.EndCurrentAsync(machine.Id)).StatusCode);

        Guid first = (await administrator.AssignedAsync(machine.Id, sequence.Id)).Id;
        (await EndCurrentAsync(machine.Id)).EnsureSuccessStatusCode();
        Guid latest = (await administrator.AssignedAsync(machine.Id, sequence.Id)).Id;

        IReadOnlyList<MachineSummary> machines = await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await viewer.GetAsync("/api/machines"));

        Assert.NotEqual(first, latest);
        Assert.Equal(latest, Assert.Single(machines, m => m.Id == machine.Id).Deployment?.Id);

        (await EndCurrentAsync(machine.Id)).EnsureSuccessStatusCode();
        machines = await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await viewer.GetAsync("/api/machines"));

        // Nothing active: the one created last.
        DeploymentSummary shown = Assert.IsType<DeploymentSummary>(Assert.Single(machines, m => m.Id == machine.Id).Deployment);
        Assert.Equal(latest, shown.Id);
        Assert.Equal(DeploymentState.Cancelled, shown.State);

        IReadOnlyList<DeploymentSummary> history = await RegisteredMachine.ReadAsync<IReadOnlyList<DeploymentSummary>>(
            await viewer.GetAsync($"/api/machines/{machine.Id}/deployments"));
        Assert.Equal([latest, first], history.Select(r => r.Id));
        Assert.Equal(latest, (await viewer.RunAsync(latest)).Summary.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/deployments/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/machines/{Guid.NewGuid()}/deployments")).StatusCode);

        DeploymentOptionsView options = await RegisteredMachine.ReadAsync<DeploymentOptionsView>(await viewer.GetAsync("/api/deployments/options"));
        Assert.False(options.DomainConfigured);
        Assert.False(options.RequireWebApproval);
        Assert.False(options.ZeroTouchEnabled);
    }

    [Fact]
    public async Task AWaitingMachineWithAnAssignmentIsNotSwept()
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));
        (await AssignAsync(machine.Id, sequence.Id)).EnsureSuccessStatusCode();
        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(2));

        await application.Services.GetRequiredService<WaitingMachineSweeper>().SweepOnceAsync(TestContext.Current.CancellationToken);

        Machine kept = await application.MachineAsync(machine.Id);
        Assert.Equal(MachineState.Pending, kept.State);
        Assert.NotNull(kept.ActiveDeploymentId);
    }

    // An agent from before task sequences is never given an image deployment, and the routes it used say so.
    [Fact]
    public async Task TheImageDeploymentRoutesAreGone()
    {
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, await application.AdministratorAsync());

        foreach (string path in new[]
        {
            AgentRoutes.Images(machine.Id),
            AgentRoutes.ImageContent(machine.Id, new string('a', 64)),
            AgentRoutes.Deployments(machine.Id),
            AgentRoutes.DeploymentReport(machine.Id, Guid.NewGuid()),
            AgentRoutes.DeploymentUnattend(machine.Id, Guid.NewGuid()),
        })
        {
            Assert.Equal(HttpStatusCode.Gone, (await machine.Agent.GetAsync(path, machine.Token)).StatusCode);
        }
    }
}
