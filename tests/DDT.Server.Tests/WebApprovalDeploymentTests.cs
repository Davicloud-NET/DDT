using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Server.Authentication;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// Under RequireWebApproval a machine needs both halves: someone signs in at it, and an operator acts on the web.
// Either order works, and an assignment on the web is such an act.
public sealed class WebApprovalDeploymentTests(WebApprovalApplication application) : IClassFixture<WebApprovalApplication>
{
    private async Task<MachineSummary> AssignAsync(Guid machineId)
    {
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        return await RegisteredMachine.ReadAsync<MachineSummary>(
            await (await application.AdministratorAsync()).PostAsync($"/api/machines/{machineId}/deployments", new AssignImageRequest(image.Id, null)));
    }

    private static async Task<AgentSignInResult> SignInAsync(DeployingMachine machine, string userName) =>
        await RegisteredMachine.ReadAsync<AgentSignInResult>(
            await machine.Agent.SignInAsync(machine.Id, machine.Token, new AgentSignInRequest(userName, DdtApplication.Password, null)));

    [Fact]
    public async Task AnAssignmentWaitsForASignInThatThenApprovesTheMachine()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);

        MachineSummary assigned = await AssignAsync(machine.Id);

        // Fresh, but nobody vouched for it at the machine.
        Assert.Equal(MachineState.Pending, assigned.State);
        Assert.Null((await machine.NextAsync()).Deployment);

        Assert.Equal(AgentSignInStatus.Succeeded, (await SignInAsync(machine, operatorName)).Status);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Approved, next.State);
        Assert.Equal(assigned.Deployment?.Id, next.Deployment?.Id);

        string subject = machine.Id.ToString("D");
        List<string?> approvals = await application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject && e.Action == AuditActions.MachineApproved)
            .Select(e => e.Detail)
            .ToListAsync(TestContext.Current.CancellationToken));

        Assert.Contains(" had assigned Test image ", Assert.Single(approvals), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASignInThenAnAssignmentApprovesTheMachine()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);

        Assert.Equal(AgentSignInStatus.Succeeded, (await SignInAsync(machine, operatorName)).Status);
        Assert.Equal(MachineState.Pending, (await machine.NextAsync()).State);

        // Seen long ago does not matter here: the sign-in at the machine happened in this generation.
        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));

        MachineSummary assigned = await AssignAsync(machine.Id);

        Assert.Equal(MachineState.Approved, assigned.State);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(DeploymentState.Assigned, next.Deployment?.State);
        Assert.False(next.CanPickImage);
    }

    [Fact]
    public async Task ASignInWithoutAnAssignmentStillWaitsForTheWeb()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);

        Assert.Equal(AgentSignInStatus.Succeeded, (await SignInAsync(machine, operatorName)).Status);

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Pending, next.State);
        Assert.False(next.CanPickImage);
    }
}
