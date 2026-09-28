// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Endpoints;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// A run's values are worked out when it starts, from the answers to its inputs, the rules and the defaults; answers come
// with an assignment or an approval, at the machine after the pick, or while the run waits at its start for them.
public sealed class RunInputTests(DomainDeploymentApplication application) : IClassFixture<DomainDeploymentApplication>
{
    private const string AccountPassword = "Given <for> one run 9";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static InputDeclaration Owner => new() { Name = "Owner", Label = "Owner", Required = true, MaxLength = 10, AskAt = InputAsk.Web };

    private static InputDeclaration Office => new()
    {
        Name = "Office",
        Label = "Office edition",
        Kind = InputKind.Choice,
        Choices = [new InputChoice("Standard"), new InputChoice("ProPlus", "Professional Plus")],
        Default = "Standard",
    };

    private static InputDeclaration Room => new() { Name = "Room", Label = "Room", Required = true, AskAt = InputAsk.Machine, MaxLength = 8 };

    private static InputDeclaration JoinAccount => new()
    {
        Name = "JoinAccount",
        Label = "Join account",
        Kind = InputKind.Account,
        AskAt = InputAsk.Web,
        Account = new AccountDestination { Domain = "corp.example" },
    };

    private static InputAnswer Given(string name, string value) => new(name, value);

    private async Task<SequenceView> AskingAsync(params InputDeclaration[] inputs)
    {
        SequenceDefinition definition = SequenceRequests.ScriptOnly() with { Inputs = inputs };
        Assert.Empty(SequenceValidator.Validate(definition.Normalised()));

        return await (await application.AdministratorAsync()).CreatedSequenceAsync(definition);
    }

    private Task<Deployment> StoredAsync(Guid runId) =>
        application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == runId, Cancellation));

    private Task<List<string>> AuditAsync(Guid runId)
    {
        string subject = runId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject)
            .OrderBy(e => e.Id)
            .Select(e => e.Action + " " + e.Detail)
            .ToListAsync(Cancellation));
    }

    private static async Task<IDictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestJson.Options, Cancellation))!.Errors;
    }

    private static async Task<AgentRunReportResult> ReportedAsync(DeployingMachine machine, Guid runId, AgentRunReport report)
    {
        await machine.ReportOkAsync(runId, report);

        return machine.LastReported!;
    }

    // A machine whose MAC address a rule sets these values for.
    private async Task<DeployingMachine> MachineWithValuesAsync(params NamedValue[] values)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);

        await administrator.CreatedRuleAsync(RuleRequests.Rule(
            $"Values for {machine.Registration.PrimaryMac}",
            new TestCondition(MachineVariableNames.MacAddress, ConditionOperator.Equals, machine.Registration.PrimaryMac),
            values: values));

        return machine;
    }

    [Fact]
    public async Task TheValuesAreWorkedOutWhenTheRunStartsWithWhereEachCameFrom()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await MachineWithValuesAsync(
            new NamedValue("Site", "Vienna"),
            new NamedValue(MachineVariableNames.ComputerName, "PC-{{PrimaryMacAddress|right:6}}"));
        SequenceView sequence = await AskingAsync(Office);
        RuleView rule = Assert.Single(await administrator.RulesAsync(), r => r.Name == $"Values for {machine.Registration.PrimaryMac}");

        Guid runId = (await administrator.AssignedAsync(machine.Id, sequence.Id)).Id;
        AgentRun assigned = (await machine.NextAsync()).Run!;

        // Nothing is worked out before the run starts, and the machine asks nothing: Office has a default.
        Assert.Null(assigned.Values);
        Assert.Null(assigned.PendingInputs);
        Assert.Null((await StoredAsync(runId)).Values);

        AgentRunReportResult started = await ReportedAsync(machine, runId, Report(DeploymentState.Running, []) with { Activity = RunActivity.Preparing });
        string name = $"PC-{machine.Registration.PrimaryMac[^6..]}";

        Assert.NotNull(started.Values);
        Assert.Equal("Vienna", started.Values["Site"]);
        Assert.Equal("Standard", started.Values["Office"]);
        Assert.Equal(name, started.Values[MachineVariableNames.ComputerName]);
        Assert.Equal("W. Europe Standard Time", started.Values["TimeZone"]);
        Assert.Null(started.InputsPending);
        Assert.Null(started.ReportAfterSeconds);

        DeploymentView view = await administrator.RunAsync(runId);
        ResolvedValue site = Assert.Single(view.Values!, value => value.Name == "Site");
        Assert.Equal((ValueSource.Rule, rule.Id, rule.Name, false), (site.Source, site.SourceId, site.SourceName, site.Overridden));
        Assert.Equal(ValueSource.SequenceDefault, Assert.Single(view.Values!, value => value.Name == "Office").Source);
        Assert.Equal(ValueSource.DeploymentDefault, Assert.Single(view.Values!, value => value.Name == "TimeZone").Source);

        // The answer file and the join take their settings from the values.
        RunInputs inputs = RunInputs.Read((await StoredAsync(runId)).Inputs!);
        Assert.Equal((name, "W. Europe Standard Time", "OU=Workstations,DC=corp,DC=example"), (inputs.ComputerName, inputs.TimeZone, inputs.DomainOrganizationalUnit));

        // The values stay as they started, whatever the rule says now; a report before the steps begin gets them again.
        await administrator.PutAsync($"{RuleRequests.Rules}/{rule.Id}", RuleRequests.Save(rule, save => save with { Values = [new NamedValue("Site", "Graz")] }));
        AgentRunReportResult again = await ReportedAsync(machine, runId, Report(DeploymentState.Running, []) with { Activity = RunActivity.Preparing });
        Assert.Equal("Vienna", again.Values!["Site"]);
        Assert.Null((await ReportedAsync(machine, runId, Running(Step(assigned.Sequence.Steps[0], StepState.Running)))).Values);

        // A resumed agent is handed them with the run.
        AgentRegistrationResult resumed = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration with { RunToken = machine.RunToken }));
        AgentRun handed = (await RegisteredMachine.ReadAsync<AgentNextResult>(await machine.Agent.NextAsync(machine.Id, resumed.Token!))).Run!;
        Assert.Equal("Vienna", handed.Values!["Site"]);
    }

    // A value that cannot be worked out keeps the run from starting at all: the agent is refused before it touches a disk.
    [Fact]
    public async Task AValueThatCannotBeWorkedOutEndsTheRunBeforeItStarts()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await MachineWithValuesAsync(new NamedValue(MachineVariableNames.ComputerName, "{{Site}}-PC"));
        SequenceView sequence = await AskingAsync();
        Guid runId = (await administrator.AssignedAsync(machine.Id, sequence.Id)).Id;

        HttpResponseMessage refused = await machine.ReportAsync(runId, Report(DeploymentState.Running, []) with { Activity = RunActivity.Preparing });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        string? error = await TestDatabase.TitleAsync(refused);
        Assert.StartsWith("The run's values have problems, so it did not start: ComputerName cannot be worked out.", error, StringComparison.Ordinal);

        Deployment failed = await StoredAsync(runId);
        Assert.Equal((DeploymentState.Failed, error, (DateTimeOffset?)null, (string?)null), (failed.State, failed.Error, failed.StartedUtc, failed.Values));
        Assert.Equal(MachineState.Failed, (await application.MachineAsync(machine.Id)).State);
        Assert.Contains($"{AuditActions.DeploymentFailed} {failed.Title} on machine {machine.Id:D} did not start: {error}", await AuditAsync(runId));
    }

    [Fact]
    public async Task AnAssignmentRefusesAnswersTheWebCannotGive()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await AskingAsync(Owner, Office, Room, JoinAccount);

        IDictionary<string, string[]> missing = await ErrorsAsync(await administrator.AssignAsync(machine.Id, sequence.Id));
        Assert.Equal(["Owner needs an answer before the run can start."], missing["answers.Owner"]);

        IDictionary<string, string[]> wrong = await ErrorsAsync(await administrator.AssignAsync(
            machine.Id,
            new AssignSequenceRequest(
                sequence.Id,
                null,
                Answers: [Given("Owner", "Somebody far too long"), Given("Office", "Home"), Given("Room", "B 12"), Given("Nobody", "x"), new InputAnswer("JoinAccount", null, "joiner", "")])));
        Assert.Equal(["Owner takes at most 10 characters."], wrong["answers.Owner"]);
        Assert.Equal(["'Home' is not one of the choices of Office edition."], wrong["answers.Office"]);
        Assert.Equal(["Room is asked at the machine, not on the web."], wrong["answers.Room"]);
        Assert.Equal(["The sequence asks nothing called Nobody. Load the page again."], wrong["answers.Nobody"]);
        Assert.Equal(2, wrong["answers.JoinAccount"].Length);
        Assert.Null((await application.MachineAsync(machine.Id)).ActiveDeploymentId);
    }

    [Fact]
    public async Task AnAssignmentTakesTheAnswersTheWebAsksAndKeepsTheAccountForTheRunOnly()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await AskingAsync(Owner, Office, Room, JoinAccount);

        MachineSummary assigned = await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.AssignAsync(
            machine.Id,
            new AssignSequenceRequest(sequence.Id, null, Answers: [Given("Owner", " Anna "), Given("office", "proplus"), new InputAnswer("JoinAccount", null, @"CORP\joiner", AccountPassword)])));
        Guid runId = assigned.Deployment!.Id;

        // The answers are kept as the inputs spell them, the account apart from them, and the audit names only the inputs.
        Deployment stored = await StoredAsync(runId);
        Assert.Equal(
            [("Owner", "Anna", false), ("Office", "ProPlus", false)],
            RunAnswer.Read(stored.Answers).Select(answer => (answer.Name, answer.Value, answer.AtMachine)));
        Assert.All(RunAnswer.Read(stored.Answers), answer => Assert.StartsWith("administrator-", answer.AnsweredBy, StringComparison.Ordinal));
        RunCredential credential = await application.QueryAsync(database => database.RunCredentials.AsNoTracking().SingleAsync(c => c.DeploymentId == runId, Cancellation));
        Assert.Equal(("JoinAccount", @"CORP\joiner", "corp.example", false), (credential.InputName, credential.UserName, credential.Domain, credential.ProvidedAtMachine));
        Assert.Contains($"{AuditActions.DeploymentInputsAnswered} Owner, Office, JoinAccount of {sequence.Name} on machine {machine.Id:D}, answered on the web.", await AuditAsync(runId));

        // The machine is asked for the room, which only it asks.
        AgentRun handed = (await machine.NextAsync()).Run!;
        Assert.Equal(["Room"], handed.PendingInputs!.Select(input => input.Name));

        await ReportedAsync(machine, runId, Report(DeploymentState.Running, []) with { Activity = RunActivity.WaitingForInput });
        AgentAnswersResult answered = await RegisteredMachine.ReadAsync<AgentAnswersResult>(
            await machine.Agent.RunAnswersAsync(machine.Id, machine.Token, runId, Given("Room", "B 12")));

        Assert.Equal("Anna", answered.Values!["Owner"]);
        Assert.Equal("ProPlus", answered.Values["Office"]);
        Assert.Equal("B 12", answered.Values["Room"]);
        Assert.DoesNotContain("JoinAccount", answered.Values.Keys);

        DeploymentView view = await administrator.RunAsync(runId);
        Assert.Equal(DeploymentState.Running, view.Summary.State);
        ResolvedValue office = Assert.Single(view.Values!, value => value is { Name: "Office", Overridden: false });
        Assert.Equal(ValueSource.Input, office.Source);
        Assert.Contains(view.Values!, value => value is { Name: "Office", Source: ValueSource.SequenceDefault, Overridden: true });
        Assert.Equal(
            [("Owner", true), ("Office", true), ("Room", true), ("JoinAccount", true)],
            view.Inputs!.Select(input => (input.Input.Name, input.Answered)));
        Assert.Equal("corp.example", Assert.Single(view.Inputs!, input => input.Input.Name == "JoinAccount").Input.Domain);

        // The password is nowhere but in the credential, encrypted: not in the run, its view, its audit or the log.
        stored = await StoredAsync(runId);
        Assert.All(
            [stored.Answers, stored.Values, stored.Inputs, stored.Variables, JsonSerializer.Serialize(view, DdtJsonContext.Default.DeploymentView), .. await AuditAsync(runId)],
            text => Assert.DoesNotContain(AccountPassword, text ?? "", StringComparison.Ordinal));
        Assert.DoesNotContain(AccountPassword, credential.ProtectedPassword, StringComparison.Ordinal);
        Assert.All(application.Log.Entries, entry => Assert.DoesNotContain(AccountPassword, entry.Message + entry.Exception, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnApprovalWithTheRulesSequenceTakesAnswersToo()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await AskingAsync(Owner);
        string model = RuleRequests.UniqueModel();
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, model));
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model);

        IDictionary<string, string[]> missing = await ErrorsAsync(await administrator.ApproveAsync(machine.Id, sequence.Id));
        Assert.Equal(["Owner needs an answer before the run can start."], missing["answers.Owner"]);
        Assert.Equal(MachineState.Pending, (await application.MachineAsync(machine.Id)).State);

        MachineSummary approved = await RegisteredMachine.ReadAsync<MachineSummary>(
            await administrator.ApproveAsync(machine.Id, sequence.Id, answers: [Given("Owner", "Ben")]));

        Assert.Equal(MachineState.Approved, approved.State);
        Assert.Equal("Ben", Assert.Single(RunAnswer.Read((await StoredAsync(approved.Deployment!.Id)).Answers)).Value);
    }

    // A zero touch or rule's run whose machine asks a required input waits at its start, still assigned, until the
    // machine or the machine's page answers; whoever answers second is refused.
    [Fact]
    public async Task ARunWaitsForWhatTheMachineAsksUntilThePageAnswersIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await AskingAsync(Room, Office);
        Guid runId = (await administrator.AssignedAsync(machine.Id, sequence.Id)).Id;
        AgentRun run = (await machine.NextAsync()).Run!;

        Assert.Equal(DeploymentState.Assigned, run.State);
        Assert.Equal(["Room", "Office"], run.PendingInputs!.Select(input => input.Name));
        Assert.Equal("Standard", run.PendingInputs![1].Default);

        // The page cannot answer before the machine says it waits.
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.AnswerAsync(machine.Id, Given("Room", "A 1"))).StatusCode);

        AgentRunReportResult waiting = await ReportedAsync(machine, runId, Report(DeploymentState.Running, []) with { Activity = RunActivity.WaitingForInput });

        Assert.Null(waiting.Values);
        Assert.Equal(["Room", "Office"], waiting.InputsPending!.Select(input => input.Name));
        Assert.Equal(5, waiting.ReportAfterSeconds);
        Assert.Null(waiting.RunToken);

        DeploymentSummary summary = (await administrator.RunAsync(runId)).Summary;
        Assert.Equal((DeploymentState.Assigned, true, RunActivity.WaitingForInput), (summary.State, summary.Waiting, summary.Activity));
        Assert.True(Assert.Single(await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await administrator.GetAsync("/api/machines")), m => m.Id == machine.Id).Deployment!.Waiting);

        // Nothing can have run before the run has its values.
        Assert.Equal(HttpStatusCode.Conflict, (await machine.ReportAsync(runId, Running(Step(run.Sequence.Steps[0], StepState.Running)))).StatusCode);

        // Only operators answer, and only what the sequence asks, as it takes it.
        using (SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.AnswerAsync(machine.Id, Given("Room", "A 1"))).StatusCode);
        }

        Assert.Equal(["Room takes at most 8 characters."], (await ErrorsAsync(await administrator.AnswerAsync(machine.Id, Given("Room", "Much too long"))))["answers.Room"]);

        DeploymentView answered = await RegisteredMachine.ReadAsync<DeploymentView>(await administrator.AnswerAsync(machine.Id, Given("Room", "A 1")));

        Assert.Equal((DeploymentState.Assigned, false), (answered.Summary.State, answered.Summary.Waiting));
        Assert.True(Assert.Single(answered.Inputs!, input => input.Input.Name == "Room").Answered);
        Assert.Contains(await AuditAsync(runId), entry => entry.StartsWith($"{AuditActions.DeploymentInputsAnswered} Room of ", StringComparison.Ordinal));

        // The console answered second.
        HttpResponseMessage late = await machine.Agent.RunAnswersAsync(machine.Id, machine.Token, runId, Given("Room", "B 2"));
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);

        // The agent's next report starts the run and hands it the values.
        AgentRunReportResult started = await ReportedAsync(machine, runId, Report(DeploymentState.Running, []) with { Activity = RunActivity.WaitingForInput });
        Assert.Equal("A 1", started.Values!["Room"]);
        Assert.NotNull(started.RunToken);
        Assert.Null(started.ReportAfterSeconds);

        HttpResponseMessage over = await administrator.AnswerAsync(machine.Id, Given("Room", "C 3"));
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Equal(DeploymentState.Running, (await over.Content.ReadFromJsonAsync<DeploymentView>(TestJson.Options, Cancellation))!.Summary.State);
    }

    [Fact]
    public async Task TheMachineAnswersWhatItAsksAndItsAnswersStartTheRun()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await AskingAsync(Owner, Room);
        Guid runId = (await administrator.AssignedAsync(machine.Id, sequence.Id, answers: [Given("Owner", "Anna")])).Id;
        await machine.NextAsync();
        await ReportedAsync(machine, runId, Report(DeploymentState.Running, []) with { Activity = RunActivity.WaitingForInput });

        AgentAnswersResult refused = await RegisteredMachine.ReadAsync<AgentAnswersResult>(
            await machine.Agent.RunAnswersAsync(machine.Id, machine.Token, runId, Given("Room", "Much too long"), Given("Owner", "Ben")));

        Assert.Null(refused.Values);
        Assert.Equal(["Room"], refused.InputsPending.Select(input => input.Name));
        Assert.Equal(
            [("Room", "Room takes at most 8 characters."), ("Owner", "Owner is asked on the web, not at the machine.")],
            refused.Problems.Select(problem => (problem.Name, problem.Message)));
        Assert.DoesNotContain("Much", (await StoredAsync(runId)).Answers ?? "", StringComparison.Ordinal);

        AgentAnswersResult answered = await RegisteredMachine.ReadAsync<AgentAnswersResult>(
            await machine.Agent.RunAnswersAsync(machine.Id, machine.Token, runId, Given("Room", "B 12")));

        Assert.Equal(("B 12", "Anna"), (answered.Values!["Room"], answered.Values["Owner"]));
        Assert.Empty(answered.InputsPending);
        Assert.Empty(answered.Problems);

        Deployment started = await StoredAsync(runId);
        Assert.Equal((DeploymentState.Running, false), (started.State, started.InputsPending));
        RunAnswer room = Assert.Single(RunAnswer.Read(started.Answers), answer => answer.Name == "Room");
        Assert.True(room.AtMachine);

        // The run started, so the page has nothing to answer, and nor has the machine.
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.AnswerAsync(machine.Id, Given("Room", "C 3"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await machine.Agent.RunAnswersAsync(machine.Id, machine.Token, runId, Given("Room", "C 3"))).StatusCode);
        Assert.Equal("B 12", (await ReportedAsync(machine, runId, Report(DeploymentState.Running, []) with { Activity = RunActivity.WaitingForInput })).Values!["Room"]);
    }

    // The page and the machine answer at the same moment: the answers are saved over those they were given to, so the
    // second save finds them changed and is refused.
    [Fact]
    public async Task OfTwoAnswersAtTheSameMomentTheSecondIsRefused()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await AskingAsync(Room);
        Guid runId = (await administrator.AssignedAsync(machine.Id, sequence.Id)).Id;
        await machine.NextAsync();
        await ReportedAsync(machine, runId, Report(DeploymentState.Running, []) with { Activity = RunActivity.WaitingForInput });

        using IServiceScope first = application.Services.CreateScope();
        using IServiceScope second = application.Services.CreateScope();
        DdtDbContext firstDatabase = first.ServiceProvider.GetRequiredService<DdtDbContext>();
        DdtDbContext secondDatabase = second.ServiceProvider.GetRequiredService<DdtDbContext>();
        Machine firstMachine = await firstDatabase.Machines.SingleAsync(m => m.Id == machine.Id, Cancellation);
        Machine secondMachine = await secondDatabase.Machines.SingleAsync(m => m.Id == machine.Id, Cancellation);
        Deployment firstRun = await firstDatabase.Deployments.SingleAsync(d => d.Id == runId, Cancellation);
        Deployment secondRun = await secondDatabase.Deployments.SingleAsync(d => d.Id == runId, Cancellation);

        RunAnswering web = await first.ServiceProvider.GetRequiredService<WaitingRuns>()
            .AnswerAsync(firstMachine, firstRun, new AnswersGiven([Given("Room", "A 1")], new Actor(null, "web", null), AtMachine: false), Cancellation);
        RunAnswering console = await second.ServiceProvider.GetRequiredService<WaitingRuns>()
            .AnswerAsync(secondMachine, secondRun, new AnswersGiven([Given("Room", "B 2")], new Actor(null, "console", null), AtMachine: true), Cancellation);

        Assert.True(await RunAnswerSaves.SaveAsync(firstDatabase, firstRun, web.Before, Cancellation));
        Assert.False(await RunAnswerSaves.SaveAsync(secondDatabase, secondRun, console.Before, Cancellation));
        Assert.Equal("A 1", Assert.Single(RunAnswer.Read((await StoredAsync(runId)).Answers)).Value);
    }

    [Fact]
    public async Task TheConsoleIsAskedWhatTheMachineAsksAfterThePick()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        SequenceView sequence = await AskingAsync(Owner, Room, Office);

        AgentSequenceChoice choice = Assert.Single(
            await RegisteredMachine.ReadAsync<IReadOnlyList<AgentSequenceChoice>>(await machine.Agent.SequencesAsync(machine.Id, machine.Token)),
            c => c.Id == sequence.Id);
        Assert.Equal([("Room", true, null), ("Office", false, "Standard")], choice.Inputs!.Select(input => (input.Name, input.Required, input.Default)));

        HttpResponseMessage unanswered = await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(sequence.Id, null, null));
        Assert.Equal(["Room needs an answer before the run can start."], (await ErrorsAsync(unanswered))["answers.Room"]);

        HttpResponseMessage tooLong = await machine.Agent.PickRunAsync(
            machine.Id,
            machine.Token,
            new AgentRunRequest(sequence.Id, null, null, Answers: [Given("Room", "Much too long")]));
        Assert.Equal("Room takes at most 8 characters.", await TestDatabase.TitleAsync(tooLong));

        AgentRun picked = await RegisteredMachine.ReadAsync<AgentRun>(await machine.Agent.PickRunAsync(
            machine.Id,
            machine.Token,
            new AgentRunRequest(sequence.Id, null, null, Answers: [Given("Room", "C 7"), Given("Office", "ProPlus")])));

        // Only the web asks the owner, so the run fails at its start rather than wait for the page.
        Assert.Null(picked.PendingInputs);
        Assert.Equal(
            [("Room", "C 7", true, operatorName), ("Office", "ProPlus", true, operatorName)],
            RunAnswer.Read((await StoredAsync(picked.Id)).Answers).Select(answer => (answer.Name, answer.Value, answer.AtMachine, answer.AnsweredBy)));

        HttpResponseMessage refused = await machine.ReportAsync(picked.Id, Report(DeploymentState.Running, []) with { Activity = RunActivity.Preparing });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            "The run did not start, because only the web asks what it lacks: Owner needs an answer before the run can start. Assign the sequence again and answer it.",
            await TestDatabase.TitleAsync(refused));
    }

    // A rule's computer name pattern names every machine it matches, so an approval can run a sequence that needs a name
    // on a machine nobody named.
    [Fact]
    public async Task AnApprovalRunsASequenceThatNeedsANameWhenARuleNamesTheMachine()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly() with
        {
            Variables = [new VariableDeclaration { Name = MachineVariableNames.ComputerName }],
        });
        string named = RuleRequests.UniqueModel();
        string unnamed = RuleRequests.UniqueModel();
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, named) with
        {
            Values = [new NamedValue(MachineVariableNames.ComputerName, "PC-{{PrimaryMacAddress|right:6}}")],
        });
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, unnamed));
        using RegisteredMachine withName = await application.RegisterModelAsync("Dell Inc.", named);
        using RegisteredMachine withoutName = await application.RegisterModelAsync("Dell Inc.", unnamed);

        HttpResponseMessage refused = await administrator.ApproveAsync(withoutName.Id, sequence.Id);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            $"{sequence.Name} names the machine with its ComputerName value, and nothing gives this machine one yet. Approve it without a sequence, then assign the sequence with a computer name.",
            await TestDatabase.TitleAsync(refused));

        MachineSummary approved = await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.ApproveAsync(withName.Id, sequence.Id));

        Assert.Equal(DeploymentState.Assigned, approved.Deployment!.State);
        Assert.Null(approved.AssignedName);

        // An assignment on the web needs no name either.
        using DeployingMachine assigned = await DeployingMachine.ApprovedAsync(application, administrator);
        await administrator.CreatedRuleAsync(RuleRequests.Rule(
            "Name by MAC",
            new TestCondition(MachineVariableNames.MacAddress, ConditionOperator.Equals, assigned.Registration.PrimaryMac),
            values: [new NamedValue(MachineVariableNames.ComputerName, "PC-{{PrimaryMacAddress|right:6}}")]));
        (await administrator.AssignAsync(assigned.Id, sequence.Id)).EnsureSuccessStatusCode();
    }

    // The console asks for the name all the same, starting with the one the rule gives, and a pick without a name typed
    // takes that one: the machine gets no name of its own.
    [Fact]
    public async Task TheConsoleStartsTheComputerNameWithTheOneARuleGives()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        SequenceView named = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly() with
        {
            Variables = [new VariableDeclaration { Name = MachineVariableNames.ComputerName }],
        });
        SequenceView plain = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        await administrator.CreatedRuleAsync(RuleRequests.Rule(
            $"Name {machine.Registration.PrimaryMac}",
            new TestCondition(MachineVariableNames.MacAddress, ConditionOperator.Equals, machine.Registration.PrimaryMac),
            values: [new NamedValue(MachineVariableNames.ComputerName, "PC-{{PrimaryMacAddress|right:6}}")]));

        IReadOnlyList<AgentSequenceChoice> choices = await RegisteredMachine.ReadAsync<IReadOnlyList<AgentSequenceChoice>>(
            await machine.Agent.SequencesAsync(machine.Id, machine.Token));

        Assert.Equal($"PC-{machine.Registration.PrimaryMac[^6..]}", Assert.Single(choices, c => c.Id == named.Id).ComputerName);
        Assert.Null(Assert.Single(choices, c => c.Id == plain.Id).ComputerName);

        AgentRun picked = await RegisteredMachine.ReadAsync<AgentRun>(
            await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(named.Id, null, null)));

        Assert.Equal(DeploymentState.Assigned, picked.State);
        Assert.Null(picked.ComputerName);
    }
}
