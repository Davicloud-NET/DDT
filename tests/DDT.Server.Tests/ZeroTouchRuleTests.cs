// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Server.Tests;

// Zero touch keeps an approval a person gave on the web. A rule is no such approval, so a listed network gives a
// machine that a rule matches nothing either.
public sealed class ZeroTouchRuleTests(ZeroTouchApplication application) : IClassFixture<ZeroTouchApplication>
{
    [Fact]
    public async Task AMachineARuleMatchesStaysWaitingOnAZeroTouchNetwork()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await application.RunnableSequenceAsync();
        string model = RuleRequests.UniqueModel();
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, model));

        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model, "10.200.7.1");
        Assert.Equal(MachineState.Pending, (await machine.NextAsync()).State);

        // Netbooted again from the listed network, as a zero touch machine is.
        AgentRegistrationResult again = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration));
        AgentNextResult next = await RegisteredMachine.ReadAsync<AgentNextResult>(await machine.Agent.NextAsync(machine.Id, again.Token!));

        Assert.Equal(MachineState.Pending, again.State);
        Assert.Equal(MachineState.Pending, next.State);
        Assert.Null(next.Deployment);
        Assert.Null(next.Run);
        Assert.Equal(SequenceResolutionSource.Rule, (await administrator.ResolutionAsync(machine.Id)).Source);
    }

    // Nor is an approval that took the rule's sequence: it belonged to that boot, so the next netboot from a listed
    // network starts over, as anywhere else.
    [Fact]
    public async Task ARulesRunDoesNotKeepTheMachineApprovedOnAZeroTouchNetwork()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await application.RunnableSequenceAsync();
        string model = RuleRequests.UniqueModel();
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, model));
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model, "10.200.7.2");

        MachineSummary approved = await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.ApproveAsync(machine.Id, sequence.Id));
        Assert.Equal(DeploymentSource.Rule, approved.Deployment?.Source);

        AgentRegistrationResult again = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration));

        Assert.Equal(MachineState.Pending, again.State);
        Assert.Equal(DeploymentState.Cancelled, (await administrator.RunAsync(approved.Deployment!.Id)).Summary.State);
        Assert.Null((await application.MachineAsync(machine.Id)).ActiveDeploymentId);
    }
}
