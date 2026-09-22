// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Machines;
using Xunit;

namespace DDT.Server.Tests;

// Under RequireWebApproval a web assignment is the web half of an approval, and a sign-in at the machine completes
// it. A rule is not a web assignment, so the sign-in still waits for an approval on the web.
public sealed class WebApprovalRuleTests(WebApprovalApplication application) : IClassFixture<WebApprovalApplication>
{
    [Fact]
    public async Task ARuleNeverCountsAsTheWebHalfOfAnApproval()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        SequenceView sequence = await application.RunnableSequenceAsync();
        string model = RuleRequests.UniqueModel();
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, model));
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model);

        Assert.Equal(AgentSignInStatus.Succeeded, (await machine.SignInAsync(operatorName)).Status);
        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Pending, next.State);
        Assert.Equal(operatorName, next.SignedInBy);
        Assert.Null(next.Deployment);
        Assert.Null(next.Run);
        Assert.False(next.CanPickSequence);
        Assert.False(Assert.Single(
            await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await administrator.GetAsync("/api/machines")),
            m => m.Id == machine.Id).EverApproved);

        // Whoever signed in there chooses the sequence at the machine, so an approval cannot also run the rule's.
        HttpResponseMessage refused = await administrator.ApproveAsync(machine.Id, sequence.Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            $"{operatorName} signed in at the machine and chooses its sequence there. Approve it without a sequence.",
            await TestDatabase.TitleAsync(refused));
        Assert.Equal(MachineState.Pending, (await application.MachineAsync(machine.Id)).State);
    }

    // Approving with the rule's sequence is no more than the web half either: without the sign-in it approves nothing.
    [Fact]
    public async Task AnApprovalWithTheRulesSequenceStillNeedsTheSignIn()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await application.RunnableSequenceAsync();
        string model = RuleRequests.UniqueModel();
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, model));
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model);

        HttpResponseMessage refused = await administrator.ApproveAsync(machine.Id, sequence.Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("Nobody has signed in at this machine yet.", await TestDatabase.TitleAsync(refused));

        Machine stored = await application.MachineAsync(machine.Id);
        Assert.Equal(MachineState.Pending, stored.State);
        Assert.Null(stored.ActiveDeploymentId);
    }
}
