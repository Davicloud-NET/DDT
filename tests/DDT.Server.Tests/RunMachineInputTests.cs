// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Deployments;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// An input only the machine asks is asked on every run the web or a rule gave the machine, also when it has a default
// or isn't required. A run picked at the console asked it at the pick.
public sealed class RunMachineInputTests(DomainDeploymentApplication application) : IClassFixture<DomainDeploymentApplication>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static InputDeclaration Desk => new() { Name = "Desk", Label = "Desk", AskAt = InputAsk.Machine, Default = "D 1" };

    private static InputDeclaration Note => new() { Name = "Note", Label = "Note", AskAt = InputAsk.Machine };

    private static InputAnswer Given(string name, string value) => new(name, value);

    private async Task<SequenceView> AskingAsync(params InputDeclaration[] inputs)
    {
        SequenceDefinition definition = SequenceRequests.ScriptOnly() with { Inputs = inputs };
        Assert.Empty(SequenceValidator.Validate(definition.Normalised()));

        return await (await application.AdministratorAsync()).CreatedSequenceAsync(definition);
    }

    private Task<Deployment> StoredAsync(Guid runId) =>
        application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == runId, Cancellation));

    private static async Task<AgentRunReportResult> WaitingAsync(DeployingMachine machine, Guid runId)
    {
        await machine.ReportOkAsync(runId, Report(DeploymentState.Running, []) with { Activity = RunActivity.WaitingForInput });

        return machine.LastReported!;
    }

    [Fact]
    public async Task AnAssignedRunWaitsForAnInputOnlyTheMachineAsksEvenWithADefault()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Guid runId = (await administrator.AssignedAsync(machine.Id, (await AskingAsync(Desk, Note)).Id)).Id;

        AgentRun handed = (await machine.NextAsync()).Run!;
        Assert.Equal([("Desk", "D 1"), ("Note", null)], handed.PendingInputs!.Select(input => (input.Name, input.Default)));

        AgentRunReportResult waiting = await WaitingAsync(machine, runId);
        Assert.Null(waiting.Values);
        Assert.Equal(["Desk", "Note"], waiting.InputsPending!.Select(input => input.Name));
        Assert.Equal((DeploymentState.Assigned, true), ((await StoredAsync(runId)).State, (await administrator.RunAsync(runId)).Summary.Waiting));

        // Another report doesn't start it either.
        Assert.Null((await WaitingAsync(machine, runId)).Values);
        Assert.Equal(DeploymentState.Assigned, (await StoredAsync(runId)).State);
    }

    // Answers left empty at the machine aren't kept, so the run starts with the default in the same request.
    [Fact]
    public async Task TheMachinesAnswersStartTheRunEvenWithTheOptionalInputsLeftEmpty()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Guid runId = (await administrator.AssignedAsync(machine.Id, (await AskingAsync(Desk, Note)).Id)).Id;
        await machine.NextAsync();
        await WaitingAsync(machine, runId);

        AgentAnswersResult answered = await RegisteredMachine.ReadAsync<AgentAnswersResult>(
            await machine.Agent.RunAnswersAsync(machine.Id, machine.Token, runId, Given("Desk", ""), Given("Note", " ")));

        Assert.Equal("D 1", answered.Values!["Desk"]);
        Assert.DoesNotContain("Note", answered.Values.Keys);
        Assert.Empty(answered.InputsPending);
        Deployment started = await StoredAsync(runId);
        Assert.Equal((DeploymentState.Running, false, null), (started.State, started.InputsPending, started.Answers));
        Assert.Equal(HttpStatusCode.Conflict, (await machine.Agent.RunAnswersAsync(machine.Id, machine.Token, runId, Given("Note", "late"))).StatusCode);
    }

    // The page can answer what only the machine asks. Until it does, its answers leave the run waiting.
    [Fact]
    public async Task ThePageStartsTheRunOnlyOnceItAnsweredWhatOnlyTheMachineAsks()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Guid runId = (await administrator.AssignedAsync(machine.Id, (await AskingAsync(Desk)).Id)).Id;
        await machine.NextAsync();
        await WaitingAsync(machine, runId);

        DeploymentView empty = await RegisteredMachine.ReadAsync<DeploymentView>(await administrator.AnswerAsync(machine.Id));
        Assert.True(empty.Summary.Waiting);
        Assert.Null((await WaitingAsync(machine, runId)).Values);

        DeploymentView answered = await RegisteredMachine.ReadAsync<DeploymentView>(await administrator.AnswerAsync(machine.Id, Given("Desk", "D 9")));
        Assert.False(answered.Summary.Waiting);
        Assert.Equal("D 9", (await WaitingAsync(machine, runId)).Values!["Desk"]);
        Assert.Equal(DeploymentState.Running, (await StoredAsync(runId)).State);
    }

    [Fact]
    public async Task ARunPickedAtTheConsoleStartsWithoutAskingAgain()
    {
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, await application.CreateUserAsync(DdtRoleNames.Operator));
        SequenceView sequence = await AskingAsync(Desk, Note);

        AgentRun picked = await RegisteredMachine.ReadAsync<AgentRun>(
            await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(sequence.Id, null, null, Answers: [Given("Note", "Hi")])));

        Assert.Null(picked.PendingInputs);
        AgentRunReportResult started = await WaitingAsync(machine, picked.Id);
        Assert.Equal(("D 1", "Hi"), (started.Values!["Desk"], started.Values["Note"]));
        Assert.Null(started.InputsPending);
    }
}
