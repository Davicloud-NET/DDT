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
using Xunit;

namespace DDT.Server.Tests;

public sealed class ZeroTouchTests(ZeroTouchApplication application) : IClassFixture<ZeroTouchApplication>
{
    // Assigned on the web while the machine was not at its prompt, so the assignment waits for its next netboot.
    private static async Task<Guid> AssignWhileAwayAsync(DdtApplication application, DeployingMachine machine)
    {
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));

        MachineSummary summary = await RegisteredMachine.ReadAsync<MachineSummary>(
            await (await application.AdministratorAsync()).PostAsync($"/api/machines/{machine.Id}/deployments", new AssignImageRequest(image.Id, null)));

        Assert.Equal(MachineState.Pending, summary.State);

        return summary.Deployment!.Id;
    }

    private Task<Deployment> StoredAsync(Guid deploymentId) =>
        application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == deploymentId, TestContext.Current.CancellationToken));

    private Task<AuditEvent> AuditAsync(Guid deploymentId, string action)
    {
        string subject = deploymentId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .SingleAsync(e => e.SubjectId == subject && e.Action == action, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ANetbootFromAListedNetworkKeepsTheAssignmentAuthorized()
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        Guid deployment = await AssignWhileAwayAsync(application, machine);
        using AgentClient lab = new(application.CreateDefaultClient(), "10.200.3.4");
        string before = machine.Token;

        AgentRegistrationResult registered = await machine.RegisterAgainAsync(lab);

        Assert.Equal(MachineState.Approved, registered.State);

        // A poll token from before the netboot, whoever registered for it, is dead although Approved accepts poll tokens.
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.NextAsync(machine.Id, before)).StatusCode);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Approved, next.State);
        Assert.Equal(deployment, next.Deployment?.Id);
        Assert.Equal(DeploymentState.Assigned, next.Deployment?.State);
        Assert.Null(next.SignedInBy);

        Machine stored = await application.MachineAsync(machine.Id);
        string subject = machine.Id.ToString("D");
        string detail = (await application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject && e.Action == AuditActions.MachineReregistered)
            .Select(e => e.Detail)
            .SingleAsync(TestContext.Current.CancellationToken)))!;

        Assert.NotNull(stored.FirstApprovedUtc);
        Assert.NotNull(stored.ApprovedByUserId);
        Assert.StartsWith("Kept approved for Test image ", detail, StringComparison.Ordinal);
        Assert.Contains(" assigned by administrator-", detail, StringComparison.Ordinal);
        Assert.EndsWith(": netbooted from 10.200.3.4 in a zero touch network.", detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("::ffff:10.200.9.9")]
    [InlineData("fd00:200::42")]
    public async Task MatchesIPv4MappedAndIPv6Addresses(string address)
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        Guid deployment = await AssignWhileAwayAsync(application, machine);
        using AgentClient lab = new(application.CreateDefaultClient(), address);

        Assert.Equal(MachineState.Approved, (await machine.RegisterAgainAsync(lab)).State);
        Assert.Equal(deployment, (await machine.NextAsync()).Deployment?.Id);
    }

    [Fact]
    public async Task ANetbootFromElsewhereWaitsForASignIn()
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        Guid deployment = await AssignWhileAwayAsync(application, machine);
        using AgentClient elsewhere = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());

        AgentRegistrationResult registered = await machine.RegisterAgainAsync(elsewhere);

        Assert.Equal(MachineState.Pending, registered.State);
        Assert.Equal(DeploymentState.Assigned, (await StoredAsync(deployment)).State);
        Assert.Null((await machine.NextAsync()).Deployment);

        // The assignment survives, and a sign-in at the machine starts it.
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        AgentSignInResult signedIn = await RegisteredMachine.ReadAsync<AgentSignInResult>(
            await machine.Agent.SignInAsync(machine.Id, machine.Token, new AgentSignInRequest(operatorName, DdtApplication.Password, null)));

        Assert.Equal(AgentSignInStatus.Succeeded, signedIn.Status);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Approved, next.State);
        Assert.Equal(deployment, next.Deployment?.Id);
        Assert.False(next.CanPickImage);
    }

    [Fact]
    public async Task ARunningDeploymentFailsWhenTheMachineStartsAgain()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        Guid deployment = (await RegisteredMachine.ReadAsync<MachineSummary>(
            await administrator.PostAsync($"/api/machines/{machine.Id}/deployments", new AssignImageRequest(image.Id, null)))).Deployment!.Id;

        await machine.ReportOkAsync(deployment, DeploymentState.Running, DeploymentStep.Download, 20);

        // Resuming with the resume token is the same agent after an outage: nothing changes.
        AgentRegistrationResult resumed = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration with { ResumeToken = machine.ResumeToken }));

        Assert.Equal(MachineState.Deploying, resumed.State);
        Assert.Equal(DeploymentState.Running, (await StoredAsync(deployment)).State);

        using AgentClient lab = new(application.CreateDefaultClient(), "10.200.7.7");
        AgentRegistrationResult restarted = await machine.RegisterAgainAsync(lab);

        Deployment failed = await StoredAsync(deployment);

        Assert.Equal(MachineState.Pending, restarted.State);
        Assert.Equal(DeploymentState.Failed, failed.State);
        Assert.Equal("The machine started again during the deployment.", failed.Error);
        Assert.Null((await application.MachineAsync(machine.Id)).ActiveDeploymentId);

        AuditEvent audit = await AuditAsync(deployment, AuditActions.DeploymentFailed);

        Assert.Equal(machine.Id, audit.ActorMachineId);
        Assert.Null(audit.ActorUserId);
        Assert.Equal("10.200.7.7", audit.SourceAddress);
        Assert.Equal($"{failed.ImageName} on machine {machine.Id:D}. The machine started again during the deployment.", audit.Detail);
    }

    // The disk and the ERASE were typed at the machine in the boot that ended, and its disk numbers can differ
    // after the restart. Whoever signs in next chooses again.
    [Fact]
    public async Task AChoiceMadeAtTheMachineEndsWhenItStartsAgain()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(
            application,
            operatorName,
            [DeployingMachine.Disk(0), DeployingMachine.Disk(1, "Samsung SSD 990 PRO")]);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        AgentDeployment picked = await RegisteredMachine.ReadAsync<AgentDeployment>(
            await machine.Agent.PickAsync(machine.Id, machine.Token, new AgentPickRequest(image.Id, 1, null)));

        using AgentClient lab = new(application.CreateDefaultClient(), "10.200.8.8");

        // Not zero touch either, although it netboots from a listed network.
        Assert.Equal(MachineState.Pending, (await machine.RegisterAgainAsync(lab)).State);

        Deployment cancelled = await StoredAsync(picked.Id);
        AuditEvent audit = await AuditAsync(picked.Id, AuditActions.DeploymentCancelled);

        Assert.Equal(DeploymentState.Cancelled, cancelled.State);
        Assert.Null(cancelled.Error);
        Assert.NotNull(cancelled.FinishedUtc);
        Assert.Null((await application.MachineAsync(machine.Id)).ActiveDeploymentId);
        Assert.Equal(machine.Id, audit.ActorMachineId);
        Assert.Equal(
            $"{image.Name} on machine {machine.Id:D}. The machine started again before the image chosen at it was installed.",
            audit.Detail);

        AgentSignInResult signedIn = await RegisteredMachine.ReadAsync<AgentSignInResult>(
            await machine.Agent.SignInAsync(machine.Id, machine.Token, new AgentSignInRequest(operatorName, DdtApplication.Password, null)));

        Assert.Equal(AgentSignInStatus.Succeeded, signedIn.Status);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Approved, next.State);
        Assert.Null(next.Deployment);
        Assert.True(next.CanPickImage);
    }

    [Fact]
    public async Task TheAssignDialogLearnsThatZeroTouchIsOn()
    {
        DeploymentOptionsView options = await RegisteredMachine.ReadAsync<DeploymentOptionsView>(
            await (await application.AdministratorAsync()).GetAsync("/api/deployments/options"));

        Assert.False(options.RequireWebApproval);
        Assert.True(options.ZeroTouchEnabled);
    }

    [Fact]
    public async Task WebApprovalTurnsZeroTouchOff()
    {
        using SettingsApplication strict = new(
            ("DDT:Machines:ZeroTouchNetworks", "10.200.0.0/16"),
            ("DDT:Machines:RequireWebApproval", "true"));
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(strict);
        await AssignWhileAwayAsync(strict, machine);
        using AgentClient lab = new(strict.CreateDefaultClient(), "10.200.3.5");

        Assert.Equal(MachineState.Pending, (await machine.RegisterAgainAsync(lab)).State);

        DeploymentOptionsView options = await RegisteredMachine.ReadAsync<DeploymentOptionsView>(
            await (await strict.AdministratorAsync()).GetAsync("/api/deployments/options"));

        Assert.True(options.RequireWebApproval);
        Assert.False(options.ZeroTouchEnabled);
    }
}
