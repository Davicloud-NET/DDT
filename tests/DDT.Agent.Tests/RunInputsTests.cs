// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// A run that a rule or zero touch started waits at its start for the inputs nobody answered yet. They're answered at
// the machine or on the machine's page, and the values the run starts with come from the server either way.
public sealed class RunInputsTests : IDisposable
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private static readonly AgentInput s_office = new(
        "Office",
        "Office",
        null,
        InputKind.Choice,
        [new InputChoice("VIE", "Vienna"), new InputChoice("GRZ", "Graz")],
        "VIE",
        true,
        null);

    private readonly FakeDeploymentTools _tools = new();
    private readonly RecordingToolRunner _toolRunner = new();
    private readonly StringWriter _lines = new();

    public void Dispose()
    {
        _tools.Dispose();
        _lines.Dispose();
    }

    [Fact]
    public async Task StartsWithTheValuesOnceTheInputsAreAnsweredAtTheMachine()
    {
        ScriptedMachineConsole console = new(_ => new ConsoleAnswer(Values: [new ConsoleInputValue("Office", "GRZ")]));
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRunAnswers(_ => new AgentAnswersResult(new Dictionary<string, string> { ["Office"] = "GRZ" }, [], []));

        RunResult result = await RunAsync(server, console);

        Assert.Equal(RunOutcome.Finished, result.Outcome);
        Assert.Equal((DeploymentState.Running, RunActivity.WaitingForInput), (server.RunReports[0].State, server.RunReports[0].Activity));
        Assert.Equal(new InputsQuestion("Install Windows", ((InputsQuestion)console.Questions[0]).Inputs, null), Assert.Single(console.Questions));
        Assert.Equal([new InputAnswer("Office", "GRZ")], Assert.Single(server.Answers).Answers);
        Assert.Equal("GRZ", Assert.Single(_toolRunner.Options).Environment!["DDT_VAR_Office"]);
        Assert.Contains("The server took the answers, and the run starts.", _lines.ToString(), StringComparison.Ordinal);
    }

    // Answered on the web in the meantime. The question at the machine goes away, and nothing is sent from here. The
    // web only answers once the machine asked, so there's a question to take away.
    [Fact]
    public async Task AnswersOnTheWebEndTheQuestionAtTheMachine()
    {
        ScriptedMachineConsole console = new();
        ScriptedAgentServer server = new();
        server.AnswerRunReports = (_, token) => console.Questions.Count == 0
            ? new AgentRunReportResult(token, "resume", null, InputsPending: [s_office], ReportAfterSeconds: 5)
            : new AgentRunReportResult(token, "resume", null, Values: new Dictionary<string, string> { ["Office"] = "VIE" });

        RunResult result = await RunAsync(server, console);

        Assert.Equal(RunOutcome.Finished, result.Outcome);
        Assert.Single(console.Questions);
        Assert.Equal(1, console.Withdrawn);
        Assert.Empty(server.Answers);
        Assert.Equal("VIE", Assert.Single(_toolRunner.Options).Environment!["DDT_VAR_Office"]);
        Assert.Contains("The inputs were answered on the web, and the run starts.", _lines.ToString(), StringComparison.Ordinal);
    }

    // A run assigned on the web that asks nothing at the machine reached the agent before it started, without values.
    // The report that started it brings them, and the steps use them from the first one on.
    [Fact]
    public async Task StartsWithTheValuesTheReportThatStartedItBrings()
    {
        ScriptedAgentServer server = new()
        {
            AnswerRunReports = (_, token) => new AgentRunReportResult(token, "resume", null, Values: new Dictionary<string, string> { ["Office"] = "GRZ" }),
        };
        ImmediateTimeProvider time = new();
        AgentLog log = new(time, _lines);
        AgentRun run = TestRuns.Run([TestRuns.Script(1) with { RebootExitCodes = [] }]);
        Assert.Null(run.Values);

        RunResult result = await TestAgents.Runner(server, _tools, log, time, new() { ToolRunner = _toolRunner, Status = TestAgents.Status(new ScriptedMachineConsole()) })
            .RunAsync(new RunRequest(s_machineId, run, null, null, new DeploymentTokens("session-0", "resume-0"), new DryRunMachineIdentityReader(1).Read()), server.Stop.Token);

        Assert.Equal(RunOutcome.Finished, result.Outcome);
        Assert.Equal((DeploymentState.Running, RunActivity.Preparing), (server.RunReports[0].State, server.RunReports[0].Activity));
        Assert.Equal("GRZ", Assert.Single(_toolRunner.Options).Environment!["DDT_VAR_Office"]);
    }

    // What the server didn't accept is asked again, with the reason at the field. That's the same whether it answered
    // with problems or refused the answers outright.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsksAgainWhatTheServerDidNotTake(bool refused)
    {
        ScriptedMachineConsole console = new(
            _ => new ConsoleAnswer(Values: [new ConsoleInputValue("Office", "GRZ")]),
            _ => new ConsoleAnswer(Values: [new ConsoleInputValue("Office", "VIE")]));
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRunAnswers(_ => refused
                ? throw new AgentRequestException("400", "Graz is closed.", System.Net.HttpStatusCode.BadRequest, new Dictionary<string, string> { ["answers.Office"] = "Graz is closed this week." })
                : new AgentAnswersResult(null, [s_office], [new InputProblem("Office", "Graz is closed this week.")]))
            .OnRunAnswers(_ => new AgentAnswersResult(new Dictionary<string, string> { ["Office"] = "VIE" }, [], []));

        RunResult result = await RunAsync(server, console);

        Assert.Equal(RunOutcome.Finished, result.Outcome);
        InputsQuestion again = (InputsQuestion)console.Questions[1];
        Assert.Equal("Graz is closed this week.", again.Inputs[0].Error);
        Assert.Equal(refused ? "Graz is closed." : null, again.Error);
        Assert.Equal(["GRZ", "VIE"], server.Answers.Select(answers => answers.Answers[0].Value));
    }

    // Answered on the web before the machine could ask, in the answer to the report that says the run waits. The run
    // starts with those values, and no question is left open at the machine.
    [Fact]
    public async Task AnswersOnTheWebBeforeTheMachineAsksLeaveNoQuestion()
    {
        ScriptedMachineConsole console = new();
        ScriptedAgentServer server = new()
        {
            AnswerRunReports = (_, token) => new AgentRunReportResult(token, "resume", null, Values: new Dictionary<string, string> { ["Office"] = "GRZ" }),
        };

        RunResult result = await RunAsync(server, console);

        Assert.Equal(RunOutcome.Finished, result.Outcome);
        Assert.Empty(console.Questions);
        Assert.Empty(server.Answers);
        Assert.Equal("GRZ", Assert.Single(_toolRunner.Options).Environment!["DDT_VAR_Office"]);
        Assert.Contains("The inputs were answered on the web, and the run starts.", _lines.ToString(), StringComparison.Ordinal);
    }

    private Task<RunResult> RunAsync(ScriptedAgentServer server, ScriptedMachineConsole console)
    {
        ImmediateTimeProvider time = new();
        AgentLog log = new(time, _lines);
        ConsoleStatus status = TestAgents.Status(console);
        SequenceDefinition definition = new(SequenceDefinition.CurrentVersion, [TestRuns.Script(1) with { RebootExitCodes = [] }])
        {
            Inputs =
            [
                new InputDeclaration
                {
                    Name = "Office",
                    Label = "Office",
                    Kind = InputKind.Choice,
                    Choices = [new InputChoice("VIE", "Vienna"), new InputChoice("GRZ", "Graz")],
                    Default = "VIE",
                    Required = true,
                },
            ],
        };
        AgentRun run = TestRuns.Run(definition.Steps) with { Sequence = definition, PendingInputs = [s_office] };
        SequenceRunner runner = TestAgents.Runner(server, _tools, log, time, new() { ToolRunner = _toolRunner, Status = status });

        return runner.RunAsync(new RunRequest(s_machineId, run, null, null, new DeploymentTokens("session-0", "resume-0"), new DryRunMachineIdentityReader(1).Read()), server.Stop.Token);
    }
}
