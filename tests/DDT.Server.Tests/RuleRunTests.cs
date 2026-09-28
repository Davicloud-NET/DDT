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
    private async Task<(RuleView Rule, SequenceView Sequence, RegisteredMachine Machine)> MatchedMachineAsync()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        string model = RuleRequests.UniqueModel();
        RuleView rule = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, model));

        return (rule, sequence, await application.RegisterModelAsync("Dell Inc.", model));
    }

    [Fact]
    public async Task ApprovingWithTheRulesSequenceRunsIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (RuleView rule, SequenceView sequence, RegisteredMachine registered) = await MatchedMachineAsync();
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
            $"{AuditActions.DeploymentAssigned} {sequence.Name}, revision 1, to machine {machine.Id:D}, chosen by rule {rule.Position + 1}, {rule.Name}, and approved by {run.RequestedBy}.",
            audit);
        Assert.Contains($"{AuditActions.MachineApproved} Was Pending. Approved to run {sequence.Name}, which a rule chose.", audit);
    }

    [Fact]
    public async Task AnApprovalIsRefusedWhenTheRulesChoseAnotherSequenceMeanwhile()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (RuleView rule, SequenceView sequence, RegisteredMachine registered) = await MatchedMachineAsync();
        using RegisteredMachine machine = registered;
        SequenceView other = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());

        HttpResponseMessage refused = await administrator.ApproveAsync(machine.Id, other.Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            $"The rules no longer choose that sequence for this machine. Rule {rule.Position + 1}, {rule.Name}, chooses {sequence.Name}. A rule only "
                + "chooses: the machine still needs an approval on the web, or someone who signs in at it, where the sequence is offered. Look at the machine again.",
            await TestDatabase.TitleAsync(refused));

        Machine stored = await application.MachineAsync(machine.Id);
        Assert.Equal(MachineState.Pending, stored.State);
        Assert.Null(stored.ActiveDeploymentId);

        // The rule is gone, so nothing chooses the sequence the page showed.
        (await administrator.DeleteAsync($"{RuleRequests.Rules}/{rule.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.ApproveAsync(machine.Id, sequence.Id)).StatusCode);
        Assert.Equal(MachineState.Pending, (await application.MachineAsync(machine.Id)).State);
    }

    [Fact]
    public async Task OnlyAWaitingMachineIsApprovedWithTheRulesSequence()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (_, SequenceView sequence, RegisteredMachine registered) = await MatchedMachineAsync();
        using RegisteredMachine machine = registered;
        (await administrator.ApproveAsync(machine.Id, null)).EnsureSuccessStatusCode();

        HttpResponseMessage refused = await administrator.ApproveAsync(machine.Id, sequence.Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("The machine is Approved.", await TestDatabase.TitleAsync(refused));
        Assert.Null((await application.MachineAsync(machine.Id)).ActiveDeploymentId);
    }

    // Nobody at the machine can say which disk to erase, so the rule's sequence waits for someone to sign in there.
    [Fact]
    public async Task AnApprovalDoesNotEraseOneOfSeveralDisks()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(
            application,
            [DeployingMachine.Disk(0), DeployingMachine.Disk(1, "Samsung SSD 990 PRO")]);
        SequenceView erasing = await application.RunnableSequenceAsync();
        await administrator.CreatedRuleAsync(RuleRequests.MacRule(erasing.Id, machine.Registration.PrimaryMac));

        HttpResponseMessage refused = await administrator.ApproveAsync(machine.Id, erasing.Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            $"{erasing.Name} erases a disk, and this machine has more than one. Approve it without a sequence, then sign in at it and choose the disk there.",
            await TestDatabase.TitleAsync(refused));

        Machine stored = await application.MachineAsync(machine.Id);
        Assert.Equal(MachineState.Pending, stored.State);
        Assert.Null(stored.ActiveDeploymentId);
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

    // The approval that took the rule's sequence only applied to that boot, like a choice made at the machine.
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
