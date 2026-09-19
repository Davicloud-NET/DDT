using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class DeploymentAssignmentTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private async Task<HttpResponseMessage> AssignAsync(Guid machineId, Guid imageId, string? computerName = null) =>
        await (await application.AdministratorAsync()).PostAsync(
            $"/api/machines/{machineId}/deployments",
            new AssignImageRequest(imageId, computerName));

    private async Task<HttpResponseMessage> EndCurrentAsync(Guid machineId) =>
        await (await application.AdministratorAsync()).DeleteAsync($"/api/machines/{machineId}/deployments/current");

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

        MachineSummary summary = await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, image.Id, "PC-0001"));

        Assert.Equal(MachineState.Approved, summary.State);
        Assert.True(summary.EverApproved);
        Assert.Equal("PC-0001", summary.AssignedName);
        Assert.Equal("Disk 0: Msft Virtual Disk, 64 GB, SCSI", summary.Disks);
        Assert.Equal(1, summary.EligibleDiskCount);
        DeploymentSummary deployment = Assert.IsType<DeploymentSummary>(summary.Deployment);
        Assert.Equal(DeploymentState.Assigned, deployment.State);
        Assert.Equal(DeploymentSource.Web, deployment.Source);
        Assert.Equal(image.Id, deployment.ImageId);
        Assert.Equal(image.Name, deployment.ImageName);
        Assert.StartsWith("administrator-", deployment.RequestedBy, StringComparison.Ordinal);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Approved, next.State);
        Assert.False(next.CanPickImage);
        Assert.False(next.DomainConfigured);
        Assert.Equal("PC-0001", next.AssignedName);
        AgentDeployment assigned = Assert.IsType<AgentDeployment>(next.Deployment);
        Assert.Equal(deployment.Id, assigned.Id);
        Assert.Equal(DeploymentState.Assigned, assigned.State);
        Assert.Equal(image.Sha256, assigned.Sha256);
        Assert.Equal(image.SizeBytes, assigned.SizeBytes);
        Assert.Equal(image.WimIndex, assigned.WimIndex);
        Assert.Equal(image.InstalledBytes, assigned.InstalledBytes);
        Assert.Null(assigned.DiskNumber);

        Assert.Equal(
            [$"{AuditActions.DeploymentAssigned} {image.Name} to machine {machine.Id:D}."],
            await AuditAsync(deployment.Id));
        Assert.Contains($"{AuditActions.MachineApproved} Was Pending. Approved by assigning {image.Name}.", await AuditAsync(machine.Id));
    }

    [Fact]
    public async Task AssigningToAMachineSeenLongAgoLeavesItWaiting()
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        Image image = await ImageAsync();

        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));

        MachineSummary summary = await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, image.Id));

        Assert.Equal(MachineState.Pending, summary.State);
        Assert.Equal(DeploymentState.Assigned, summary.Deployment?.State);

        // Whoever holds the tokens of a registration seen that long ago may not be the machine at the prompt.
        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Pending, next.State);
        Assert.Null(next.Deployment);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await machine.Agent.LogAsync(machine.Id, next.Token, new AgentLogBatch([new AgentLogLine(DateTimeOffset.UtcNow, AgentLogLevel.Information, "x")]))).StatusCode);
    }

    [Fact]
    public async Task AFailedOrDoneMachineIsApprovedAgainByAnAssignment()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await ImageAsync();

        Guid first = (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, image.Id))).Deployment!.Id;
        await machine.ReportOkAsync(first, DeploymentState.Failed, DeploymentStep.Partition, error: "No internal disk was found.");

        Assert.Equal(MachineState.Failed, (await machine.NextAsync()).State);

        MachineSummary again = await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, image.Id));

        Assert.Equal(MachineState.Approved, again.State);
        Assert.NotEqual(first, again.Deployment!.Id);
        Assert.Equal(again.Deployment.Id, (await machine.NextAsync()).Deployment?.Id);
    }

    // Done refuses every token, and the tokens the Done report handed back must stay dead once an assignment
    // makes the machine Approved again: the agent that held them rebooted.
    [Fact]
    public async Task AnAssignmentAfterDoneKeepsTheFinishedRunsTokensDead()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await ImageAsync();

        Guid first = (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, image.Id))).Deployment!.Id;
        await machine.ReportOkAsync(first, DeploymentState.Running, DeploymentStep.Partition);
        await machine.ReportOkAsync(first, DeploymentState.Done, DeploymentStep.Reboot, 100);

        string sessionToken = machine.Token;
        string resumeToken = machine.ResumeToken;

        Assert.Equal(MachineState.Approved, (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, image.Id))).State);
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.NextAsync(machine.Id, sessionToken)).StatusCode);

        AgentRegistrationResult resumed = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration with { ResumeToken = resumeToken }));

        Assert.Equal(MachineState.Pending, resumed.State);
    }

    [Fact]
    public async Task RefusesWhatTheMachineCannotDeploy()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine twoDisks = await DeployingMachine.ApprovedAsync(
            application,
            administrator,
            [DeployingMachine.Disk(0), DeployingMachine.Disk(1, "Samsung SSD 990 PRO")]);
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await ImageAsync();
        Image arm = await ImageAsync("arm64");
        Image unknown = await ImageAsync(null);

        HttpResponseMessage disks = await AssignAsync(twoDisks.Id, image.Id);
        Assert.Equal(HttpStatusCode.Conflict, disks.StatusCode);
        Assert.Equal("This machine has more than one disk. Sign in at it and choose the disk there.", await TestDatabase.TitleAsync(disks));

        HttpResponseMessage armImage = await AssignAsync(machine.Id, arm.Id);
        Assert.Equal(HttpStatusCode.Conflict, armImage.StatusCode);
        Assert.Equal($"{arm.Name} is an arm64 image, and DDT deploys only x64 Windows. Choose an x64 image.", await TestDatabase.TitleAsync(armImage));

        HttpResponseMessage noArchitecture = await AssignAsync(machine.Id, unknown.Id);
        Assert.Equal(HttpStatusCode.Conflict, noArchitecture.StatusCode);
        Assert.Contains("does not say which processor", await TestDatabase.TitleAsync(noArchitecture), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NotFound, (await AssignAsync(machine.Id, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AssignAsync(Guid.NewGuid(), image.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(machine.Id, image.Id, "PC_0001")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(machine.Id, image.Id, "1234")).StatusCode);

        (await AssignAsync(machine.Id, image.Id)).EnsureSuccessStatusCode();

        HttpResponseMessage twice = await AssignAsync(machine.Id, image.Id);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("This machine already has a deployment. Cancel it before assigning another image.", await TestDatabase.TitleAsync(twice));

        Assert.Null((await application.MachineAsync(machine.Id)).AssignedName);
    }

    [Fact]
    public async Task RefusesARejectedOrDeployingMachine()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine deploying = await DeployingMachine.ApprovedAsync(application, administrator);
        using DeployingMachine rejected = await DeployingMachine.RegisterAsync(application);
        Image image = await ImageAsync();

        Guid deployment = (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(deploying.Id, image.Id))).Deployment!.Id;
        await deploying.ReportOkAsync(deployment, DeploymentState.Running, DeploymentStep.Partition);
        (await administrator.PostAsync($"/api/machines/{rejected.Id}/reject")).EnsureSuccessStatusCode();

        HttpResponseMessage busy = await AssignAsync(deploying.Id, image.Id);
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        Assert.Equal(
            "The machine is installing an image. Stop that deployment before assigning another image.",
            await TestDatabase.TitleAsync(busy));
        Assert.Equal(
            "The machine was rejected, so it cannot be given an image. Assign the image to another machine.",
            await TestDatabase.TitleAsync(await AssignAsync(rejected.Id, image.Id)));
    }

    [Fact]
    public async Task CancellingAnAssignmentLeavesTheMachineAsItWas()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await ImageAsync();

        Guid deployment = (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, image.Id))).Deployment!.Id;

        MachineSummary cancelled = await RegisteredMachine.ReadAsync<MachineSummary>(await EndCurrentAsync(machine.Id));

        Assert.Equal(MachineState.Approved, cancelled.State);
        Assert.Equal(deployment, cancelled.Deployment?.Id);
        Assert.Equal(DeploymentState.Cancelled, cancelled.Deployment?.State);
        Assert.NotNull(cancelled.Deployment?.FinishedUtc);
        Assert.Null((await machine.NextAsync()).Deployment);
        Assert.Equal(HttpStatusCode.Conflict, (await machine.ReportAsync(deployment, DeploymentState.Running, DeploymentStep.Partition)).StatusCode);

        HttpResponseMessage again = await EndCurrentAsync(machine.Id);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.StartsWith("This machine has no deployment that is assigned or running.", await TestDatabase.TitleAsync(again), StringComparison.Ordinal);
        Assert.Contains(await AuditAsync(deployment), a => a.StartsWith(AuditActions.DeploymentCancelled + " ", StringComparison.Ordinal));

        // Nothing blocks a new assignment.
        (await AssignAsync(machine.Id, image.Id)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task StoppingARunningDeploymentFailsItAndCutsOffTheAgent()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await ImageAsync();

        Guid deployment = (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, image.Id))).Deployment!.Id;
        await machine.ReportOkAsync(deployment, DeploymentState.Running, DeploymentStep.Download, 40);

        MachineSummary stopped = await RegisteredMachine.ReadAsync<MachineSummary>(await EndCurrentAsync(machine.Id));

        Assert.Equal(MachineState.Failed, stopped.State);
        Assert.Equal(DeploymentState.Failed, stopped.Deployment?.State);
        Assert.StartsWith("Stopped by administrator-", stopped.Deployment?.Error, StringComparison.Ordinal);
        Assert.Contains(
            $"{AuditActions.DeploymentFailed} {image.Name} on machine {machine.Id:D}. {stopped.Deployment?.Error}",
            await AuditAsync(deployment));
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.ReportAsync(deployment, DeploymentState.Running, DeploymentStep.Download, 50)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.NextAsync(machine.Id, machine.Token)).StatusCode);

        // Its resume token died with the generation, so the agent starts over.
        AgentRegistrationResult again = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration with { ResumeToken = machine.ResumeToken }));

        Assert.Equal(MachineState.Pending, again.State);
    }

    [Fact]
    public async Task RejectingAMachineEndsItsDeployment()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine assigned = await DeployingMachine.ApprovedAsync(application, administrator);
        using DeployingMachine running = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await ImageAsync();

        Guid assignment = (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(assigned.Id, image.Id))).Deployment!.Id;
        Guid deployment = (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(running.Id, image.Id))).Deployment!.Id;
        await running.ReportOkAsync(deployment, DeploymentState.Running, DeploymentStep.Apply, 10);

        MachineSummary cancelled = await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.PostAsync($"/api/machines/{assigned.Id}/reject"));
        MachineSummary failed = await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.PostAsync($"/api/machines/{running.Id}/reject"));

        Assert.Equal(MachineState.Rejected, cancelled.State);
        Assert.Equal(DeploymentState.Cancelled, cancelled.Deployment?.State);
        Assert.Equal(MachineState.Rejected, failed.State);
        Assert.Equal(DeploymentState.Failed, failed.Deployment?.State);
        Assert.StartsWith("Rejected by administrator-", failed.Deployment?.Error, StringComparison.Ordinal);

        // The administrator who assigned both images also rejected both machines.
        string administratorName = cancelled.Deployment!.RequestedBy!;
        Assert.Contains(
            $"{AuditActions.DeploymentCancelled} {image.Name} on machine {assigned.Id:D}. The machine was rejected by {administratorName}.",
            await AuditAsync(assignment));
        Assert.Contains(
            $"{AuditActions.DeploymentFailed} {image.Name} on machine {running.Id:D}. Rejected by {administratorName}.",
            await AuditAsync(deployment));
        Assert.Null((await application.MachineAsync(running.Id)).ActiveDeploymentId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await running.ReportAsync(deployment, DeploymentState.Running, DeploymentStep.Apply, 20)).StatusCode);

        // Rejected stays final: a second rejection is refused.
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.PostAsync($"/api/machines/{running.Id}/reject")).StatusCode);
    }

    [Fact]
    public async Task AMachineWhoseDeploymentFailedCanBeRejected()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await ImageAsync();

        Guid deployment = (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, image.Id))).Deployment!.Id;
        await machine.ReportOkAsync(deployment, DeploymentState.Running, DeploymentStep.Partition);
        await machine.ReportOkAsync(deployment, DeploymentState.Failed, DeploymentStep.Apply, 30, "Apply failed.");

        MachineSummary rejected = await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.PostAsync($"/api/machines/{machine.Id}/reject"));

        Assert.Equal(MachineState.Rejected, rejected.State);
        Assert.Equal(deployment, rejected.Deployment?.Id);
        Assert.Equal(DeploymentState.Failed, rejected.Deployment?.State);
        Assert.Equal("Apply failed.", rejected.Deployment?.Error);
    }

    // Both leave the machine Approved, so only the concurrency token on ActiveDeploymentId stops the second one.
    [Fact]
    public async Task TwoAssignmentsRacingForOneMachineLeaveOneDeployment()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await ImageAsync();

        using IServiceScope first = application.Services.CreateScope();
        using IServiceScope second = application.Services.CreateScope();
        DdtDbContext firstDatabase = first.ServiceProvider.GetRequiredService<DdtDbContext>();
        DdtDbContext secondDatabase = second.ServiceProvider.GetRequiredService<DdtDbContext>();
        Machine firstMachine = await firstDatabase.Machines.SingleAsync(m => m.Id == machine.Id, cancellationToken);
        Machine secondMachine = await secondDatabase.Machines.SingleAsync(m => m.Id == machine.Id, cancellationToken);

        DeploymentDecision firstDecision = await first.ServiceProvider.GetRequiredService<DeploymentService>()
            .AssignAsync(firstMachine, new AssignImageRequest(image.Id, null), null, "first", null, cancellationToken);
        DeploymentDecision secondDecision = await second.ServiceProvider.GetRequiredService<DeploymentService>()
            .AssignAsync(secondMachine, new AssignImageRequest(image.Id, null), null, "second", null, cancellationToken);

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
        ImageStore store = application.Services.GetRequiredService<ImageStore>();
        Task<HttpResponseMessage> assigning;

        await store.LibraryLock.WaitAsync(cancellationToken);

        try
        {
            assigning = AssignAsync(machine.Id, image.Id);
            await Task.WhenAny(assigning, Task.Delay(TimeSpan.FromSeconds(1), cancellationToken));
            Assert.False(assigning.IsCompleted);

            await application.QueryAsync(database => database.Images.Where(i => i.Id == image.Id).ExecuteDeleteAsync(cancellationToken));
        }
        finally
        {
            store.LibraryLock.Release();
        }

        HttpResponseMessage response = await assigning;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("The image no longer exists. Load the page again and choose another image.", await TestDatabase.TitleAsync(response));
        Assert.Null((await application.MachineAsync(machine.Id)).ActiveDeploymentId);
    }

    [Fact]
    public async Task OnlyOperatorsAssignAndEveryViewerSeesTheDeployment()
    {
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await ImageAsync();

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.PostAsync($"/api/machines/{machine.Id}/deployments", new AssignImageRequest(image.Id, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/machines/{machine.Id}/deployments/current")).StatusCode);

        Guid deployment = (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, image.Id))).Deployment!.Id;
        (await EndCurrentAsync(machine.Id)).EnsureSuccessStatusCode();
        Guid latest = (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, image.Id))).Deployment!.Id;

        IReadOnlyList<MachineSummary> machines = await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await viewer.GetAsync("/api/machines"));

        Assert.NotEqual(deployment, latest);
        Assert.Equal(latest, Assert.Single(machines, m => m.Id == machine.Id).Deployment?.Id);

        (await EndCurrentAsync(machine.Id)).EnsureSuccessStatusCode();
        machines = await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await viewer.GetAsync("/api/machines"));

        // Nothing active: the one that ended last.
        DeploymentSummary shown = Assert.IsType<DeploymentSummary>(Assert.Single(machines, m => m.Id == machine.Id).Deployment);
        Assert.Equal(latest, shown.Id);
        Assert.Equal(DeploymentState.Cancelled, shown.State);

        DeploymentOptionsView options = await RegisteredMachine.ReadAsync<DeploymentOptionsView>(await viewer.GetAsync("/api/deployments/options"));
        Assert.False(options.DomainConfigured);
        Assert.False(options.RequireWebApproval);
        Assert.False(options.ZeroTouchEnabled);
    }

    [Fact]
    public async Task AWaitingMachineWithAnAssignmentIsNotSwept()
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        Image image = await ImageAsync();

        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));
        (await AssignAsync(machine.Id, image.Id)).EnsureSuccessStatusCode();
        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(2));

        await application.Services.GetRequiredService<WaitingMachineSweeper>().SweepOnceAsync(TestContext.Current.CancellationToken);

        Machine kept = await application.MachineAsync(machine.Id);
        Assert.Equal(MachineState.Pending, kept.State);
        Assert.NotNull(kept.ActiveDeploymentId);
    }
}
