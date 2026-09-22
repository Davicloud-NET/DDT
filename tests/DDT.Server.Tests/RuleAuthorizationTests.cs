// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Server.Tests;

// A rule chooses a sequence for machines by facts they report about themselves, which anyone can fake. It must never
// be what lets a machine run anything.
public sealed class RuleAuthorizationTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task AWaitingMachineThatRulesMatchStaysWaitingWithNothingToRun()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await application.RunnableSequenceAsync();
        string model = RuleRequests.UniqueModel();
        string mac = RuleRequests.RandomMac();
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, model));
        await administrator.CreatedRuleAsync(RuleRequests.MacRule(sequence.Id, mac));

        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model, macs: mac);
        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Pending, next.State);
        Assert.Null(next.Deployment);
        Assert.Null(next.Run);
        Assert.False(next.CanPickSequence);

        MachineSummary listed = Assert.Single(
            await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await administrator.GetAsync("/api/machines")),
            m => m.Id == machine.Id);
        Assert.Equal(MachineState.Pending, listed.State);
        Assert.Null(listed.Deployment);
        Assert.False(listed.EverApproved);

        // The rules did match; they only chose.
        Assert.Equal(SequenceResolutionSource.MacRule, (await administrator.ResolutionAsync(machine.Id)).Source);
    }
}
