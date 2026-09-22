// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// A rule's sequence runs only when an operator approves the machine with it, having seen which one it is.
public sealed class RuleRunTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private async Task<(AssignmentRuleView Rule, SequenceView Sequence, RegisteredMachine Machine)> MatchedMachineAsync()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        string model = RuleRequests.UniqueModel();
        AssignmentRuleView rule = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, model));

        return (rule, sequence, await application.RegisterModelAsync("Dell Inc.", model));
    }

    [Fact]
    public async Task ApprovingWithTheRulesSequenceRunsIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (AssignmentRuleView rule, SequenceView sequence, RegisteredMachine registered) = await MatchedMachineAsync();
        using RegisteredMachine machine = registered;

        MachineSummary approved = await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.ApproveAsync(machine.Id, sequence.Id));

        Assert.Equal(MachineState.Approved, approved.State);
        DeploymentSummary run = Assert.IsType<DeploymentSummary>(approved.Deployment);
        Assert.Equal(DeploymentSource.Rule, run.Source);
        Assert.Equal(DeploymentState.Assigned, run.State);
        Assert.Equal(sequence.Id, run.SequenceId);
        Assert.StartsWith("administrator-", run.RequestedBy, StringComparison.Ordinal);
        Assert.Equal(rule.Id, (await administrator.RunAsync(run.Id)).RuleId);

        string runSubject = run.Id.ToString("D");
        string machineSubject = machine.Id.ToString("D");
        List<string> audit = await application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == runSubject || e.SubjectId == machineSubject)
            .OrderBy(e => e.Id)
            .Select(e => e.Action + " " + e.Detail)
            .ToListAsync(TestContext.Current.CancellationToken));

        Assert.Contains(
            $"{AuditActions.DeploymentAssigned} {sequence.Name}, revision 1, to machine {machine.Id:D}, chosen by the rule for model {rule.Model} of any maker and approved by {run.RequestedBy}.",
            audit);
        Assert.Contains($"{AuditActions.MachineApproved} Was Pending. Approved to run {sequence.Name}, which a rule chose.", audit);
    }

    [Fact]
    public async Task AnApprovalIsRefusedWhenTheRulesChoseAnotherSequenceMeanwhile()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (AssignmentRuleView rule, SequenceView sequence, RegisteredMachine registered) = await MatchedMachineAsync();
        using RegisteredMachine machine = registered;
        SequenceView other = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());

        HttpResponseMessage refused = await administrator.ApproveAsync(machine.Id, other.Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.StartsWith("The rules no longer choose that sequence for this machine. The rule for ", await TestDatabase.TitleAsync(refused), StringComparison.Ordinal);

        Machine stored = await application.MachineAsync(machine.Id);
        Assert.Equal(MachineState.Pending, stored.State);
        Assert.Null(stored.ActiveDeploymentId);

        // The rule is gone, so nothing chooses the sequence the page showed.
        (await administrator.DeleteAsync($"{RuleRequests.Rules}/{rule.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.ApproveAsync(machine.Id, sequence.Id)).StatusCode);
        Assert.Equal(MachineState.Pending, (await application.MachineAsync(machine.Id)).State);
    }

    [Fact]
    public async Task AnApprovalWithoutASequenceRunsNothing()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (_, _, RegisteredMachine registered) = await MatchedMachineAsync();
        using RegisteredMachine machine = registered;

        MachineSummary approved = await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.ApproveAsync(machine.Id, null));

        Assert.Equal(MachineState.Approved, approved.State);
        Assert.Null(approved.Deployment);
        Assert.Null((await application.MachineAsync(machine.Id)).ActiveDeploymentId);
    }

    [Fact]
    public async Task ARulesSequenceWithProblemsDoesNotRun()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView broken = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(Guid.Empty));
        string model = RuleRequests.UniqueModel();
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(broken.Id, model));
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model);

        HttpResponseMessage refused = await administrator.ApproveAsync(machine.Id, broken.Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.StartsWith($"{broken.Name} has ", await TestDatabase.TitleAsync(refused), StringComparison.Ordinal);
        Assert.Equal(MachineState.Pending, (await application.MachineAsync(machine.Id)).State);
    }

    // The approval that took the rule's sequence belonged to that boot, like a choice at the machine.
    [Fact]
    public async Task ARulesRunEndsWhenTheMachineStartsAgainBeforeItBegan()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (_, SequenceView sequence, RegisteredMachine registered) = await MatchedMachineAsync();
        using RegisteredMachine machine = registered;

        Guid run = (await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.ApproveAsync(machine.Id, sequence.Id))).Deployment!.Id;

        await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(machine.Registration));

        Assert.Equal(DeploymentState.Cancelled, (await administrator.RunAsync(run)).Summary.State);
        Assert.Equal(MachineState.Pending, (await application.MachineAsync(machine.Id)).State);
    }
}
