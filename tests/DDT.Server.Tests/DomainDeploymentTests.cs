// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Deployments;
using DDT.Server.Settings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class DomainDeploymentTests(DomainDeploymentApplication application) : IClassFixture<DomainDeploymentApplication>
{
    // Installs Windows and joins the domain in it, like the Install Windows template does when there's a domain.
    private async Task<SequenceView> DomainSequenceAsync()
    {
        Guid imageId = (await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096))).Id;

        return await (await application.AdministratorAsync()).CreatedSequenceAsync(SequenceRequests.Definition(
        [
            .. SequenceRequests.Minimal(imageId).Steps,
            new WriteUnattendStep { Id = Guid.NewGuid(), Name = "Answer file", LocalAdministrator = true },
            new JoinDomainStep { Id = Guid.NewGuid(), Name = "Join", RebootAfter = true },
        ]));
    }

    private async Task<HttpResponseMessage> AssignAsync(Guid machineId, Guid sequenceId, string? computerName) =>
        await (await application.AdministratorAsync()).AssignAsync(machineId, sequenceId, computerName);

    // The configuration locks the fields it sets. So the snapshot every deployment reads holds its values.
    [Fact]
    public void DeploymentsUseTheConfiguredSettings()
    {
        DeploymentOptions options = application.Services.GetRequiredService<DdtSettings>().Current.Deployment;

        Assert.Equal("corp.example", options.Domain.Name);
        Assert.Equal(DomainDeploymentApplication.JoinPassword, options.Domain.Password);
    }

    [Fact]
    public async Task TheAgentAndTheWebUiLearnThatADomainIsConfigured()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        SignedInClient administrator = await application.AdministratorAsync();

        AgentNextResult next = await machine.NextAsync();

        Assert.True(next.DomainConfigured);
        Assert.Null(next.AssignedName);
        Assert.True((await RegisteredMachine.ReadAsync<DeploymentOptionsView>(await administrator.GetAsync("/api/deployments/options"))).DomainConfigured);
    }

    // The console at the machine starts in the language the deployment defaults name, even while the machine waits.
    [Fact]
    public async Task TheRegistrationSaysWhichLanguageTheConsoleSpeaks()
    {
        AgentClient agent = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());

        using HttpResponseMessage response = await agent.RegisterAsync(AgentClient.Registration(Guid.NewGuid().ToString("D"), "02DD0000C0DE"));
        AgentRegistrationResult registration = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(response);

        Assert.Equal(MachineState.Pending, registration.State);
        Assert.Equal("de", registration.ConsoleLanguage);
    }

    // Anyone who presents a waiting machine's UUID and MAC can poll as that machine.
    // So a waiting machine learns neither its name nor the domain.
    [Fact]
    public async Task AWaitingMachineLearnsItsNameAndTheDomainOnlyOnceAuthorized()
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        SequenceView sequence = await DomainSequenceAsync();

        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));
        Assert.Equal(MachineState.Pending, (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, sequence.Id, "PC-0006"))).State);

        AgentNextResult waiting = await machine.NextAsync();

        Assert.Equal(MachineState.Pending, waiting.State);
        Assert.False(waiting.DomainConfigured);
        Assert.Null(waiting.AssignedName);
        Assert.Null(waiting.Run);

        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        AgentSignInResult signedIn = await RegisteredMachine.ReadAsync<AgentSignInResult>(
            await machine.Agent.SignInAsync(machine.Id, machine.Token, new AgentSignInRequest(operatorName, DdtApplication.Password, null)));

        Assert.Equal(AgentSignInStatus.Succeeded, signedIn.Status);

        AgentNextResult approved = await machine.NextAsync();

        Assert.Equal(MachineState.Approved, approved.State);
        Assert.True(approved.DomainConfigured);
        Assert.Equal("PC-0006", approved.AssignedName);
    }

    [Fact]
    public async Task AMachineJoiningTheDomainNeedsAName()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await DomainSequenceAsync();

        HttpResponseMessage nameless = await AssignAsync(machine.Id, sequence.Id, null);
        Assert.Equal(HttpStatusCode.BadRequest, nameless.StatusCode);
        Assert.Contains("computer name", await nameless.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);

        MachineSummary named = await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, sequence.Id, " PC-0003 "));
        Assert.Equal("PC-0003", named.AssignedName);

        // Once it has a name, a later assignment keeps it.
        (await administrator.EndCurrentAsync(machine.Id)).EnsureSuccessStatusCode();
        Assert.Equal("PC-0003", (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, sequence.Id, null))).AssignedName);
    }

    // The name is only for the join. So a sequence that joins nothing doesn't need one, domain or not.
    [Fact]
    public async Task ASequenceThatJoinsNothingNeedsNoName()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());

        MachineSummary assigned = await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, sequence.Id, null));

        Assert.Null(assigned.AssignedName);
        Assert.Equal(DeploymentState.Assigned, assigned.Deployment?.State);
    }

    [Fact]
    public async Task AMachineJoiningTheDomainNeedsANameWhenChosenAtTheMachine()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        SequenceView sequence = await DomainSequenceAsync();

        AgentSequenceChoice choice = Assert.Single(
            await RegisteredMachine.ReadAsync<IReadOnlyList<AgentSequenceChoice>>(await machine.Agent.SequencesAsync(machine.Id, machine.Token)),
            c => c.Id == sequence.Id);
        Assert.True(choice.NeedsComputerName);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(sequence.Id, 0, null))).StatusCode);

        AgentRun picked = await RegisteredMachine.ReadAsync<AgentRun>(
            await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(sequence.Id, 0, "PC-0004")));

        Assert.Equal(DeploymentState.Assigned, picked.State);
        Assert.Equal("PC-0004", picked.ComputerName);
        Assert.Equal("PC-0004", (await machine.NextAsync()).AssignedName);
    }

    // A rule doesn't give a name. So an approval can't run the rule's domain sequence on a machine without one.
    [Fact]
    public async Task AnApprovalCannotRunARulesDomainSequenceWithoutAName()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await DomainSequenceAsync();
        string model = RuleRequests.UniqueModel();
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, model));
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model);

        HttpResponseMessage refused = await administrator.ApproveAsync(machine.Id, sequence.Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            $"{sequence.Name} joins the domain, and this machine has no name yet. Approve it without a sequence, then assign the sequence with a computer name.",
            await TestDatabase.TitleAsync(refused));
        Assert.Equal(MachineState.Pending, (await application.MachineAsync(machine.Id)).State);
    }
}
