// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;
using System.Security.Principal;
using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class PauseStepRunnerTests
{
    private static readonly PauseStep s_pause = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a1"),
        Name = "Plug in the dock",
        Message = "Plug the dock into {{ComputerName}}.",
    };

    private readonly ScriptedAgentServer _server = new();
    private readonly ImmediateTimeProvider _time = new();
    private readonly StringWriter _console = new();
    private readonly AgentLog _log;
    private readonly RunHeartbeat _heartbeat;

    public PauseStepRunnerTests()
    {
        _log = new AgentLog(_time, _console);
        _heartbeat = new RunHeartbeat(
            _server,
            _log,
            new RunSession(StepRunnerFixture.MachineId, TestRuns.Run([s_pause]), new DeploymentTokens("session", "resume")),
            _ => Task.CompletedTask,
            Timeout.InfiniteTimeSpan,
            _time);

        // The engine saved the pause's second visit as running.
        SequenceDefinition definition = new(SequenceDefinition.CurrentVersion, [s_pause]);
        _heartbeat.Update(SequenceStates.Start(TestRuns.RunId, definition) with { Steps = [new StepRunState(s_pause.Id, StepState.Running, null, Pass: 2)] });
    }

    // The pause is reported at once with its message worked out, and the question at the machine continues it.
    [Fact]
    public async Task GoesOnOnceSomeoneContinuesAtTheMachine()
    {
        ScriptedMachineConsole console = new(_ => new ConsoleAnswer(Continue: true));

        StepResult result = await RunAsync(console);

        Assert.Equal(StepOutcome.Done, result.Outcome);
        Assert.Equal(new PauseQuestion("Plug in the dock", "Plug the dock into PC-042."), Assert.Single(console.Questions));
        AgentRunReport report = Assert.Single(_server.RunReports);
        Assert.Equal((RunActivity.Paused, "Plug the dock into PC-042.", s_pause.Id), (report.Activity, report.PauseMessage, report.CurrentStepId));
        Assert.Equal(RunActivity.Step, _heartbeat.Activity);
        Assert.Contains("Someone continued the run at this machine.", _console.ToString(), StringComparison.Ordinal);
    }

    // A continue on the web for an earlier visit leaves the pause waiting; the one for this visit ends it, and the question
    // at the machine goes away.
    [Fact]
    public async Task GoesOnOnceSomeoneContinuesThisVisitOnTheWeb()
    {
        using CancellationTokenSource run = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        ScriptedMachineConsole console = new();
        int reports = 0;
        _server.AnswerRunReports = (_, token) => new AgentRunReportResult(
            token,
            "resume",
            null,
            ContinueStepId: s_pause.Id,
            ContinuePass: Interlocked.Increment(ref reports) < 3 ? 1 : 2);
        _heartbeat.Start(run, run.Token);

        StepResult result = await RunAsync(console);
        await _heartbeat.StopAsync();

        Assert.Equal(StepOutcome.Done, result.Outcome);
        Assert.True(_server.RunReports.Count >= 3);
        Assert.Equal(1, console.Withdrawn);
        Assert.Contains("Someone continued the run on the web.", _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GoesOnByItselfOnceItsTimeIsUp()
    {
        ScriptedMachineConsole console = new();

        StepResult result = await RunAsync(console, s_pause with { ContinueAfterMinutes = 10 });

        Assert.Equal(StepOutcome.Done, result.Outcome);
        Assert.Contains(TimeSpan.FromMinutes(10), _time.Delays);
        Assert.Equal(1, console.Withdrawn);
        Assert.Contains("The run pauses until someone continues it at this machine or on the web, or for 10 minutes at most.", _console.ToString(), StringComparison.Ordinal);
        Assert.Contains("The run goes on after 10 minutes.", _console.ToString(), StringComparison.Ordinal);
    }

    // Where nobody can type, the web or the time decides.
    [Fact]
    public async Task DoesNotAskWhereNobodyCanAnswer()
    {
        ScriptedMachineConsole console = new() { CanAsk = false };
        _server.AnswerRunReports = (_, token) => new AgentRunReportResult(token, "resume", null, ContinueStepId: s_pause.Id, ContinuePass: 2);

        StepResult result = await RunAsync(console);

        Assert.Equal(StepOutcome.Done, result.Outcome);
        Assert.Empty(console.Questions);
    }

    // The console in DDT's session asks even while none is connected, as Windows starts the session when it will, and a
    // console that connects later gets the question and continues the run.
    [Fact]
    public async Task AsksTheConsoleOfDdtsSessionBeforeItConnects()
    {
        string pipe = ConsolePipe.NewName();
        await using SessionMachineConsole session = new(_log, TestAgents.Version);

        Task<StepResult> pausing = RunAsync(session);
        session.Start(() => SessionMachineConsole.CreatePipe(pipe, WindowsIdentity.GetCurrent().User!));
        await using ConsoleClient client = await ConsoleClient.ConnectAsync(
            new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous),
            new HelloMessage(HelloMessage.CurrentVersion, "Test console"),
            null,
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        QuestionMessage? question = null;

        while (question is null)
        {
            question = (await client.ReceiveAsync(timeout.Token) ?? throw new InvalidOperationException("The pipe closed before the question came.")) as QuestionMessage;
        }

        Assert.Equal(new PauseQuestion("Plug in the dock", "Plug the dock into PC-042."), question.Question);
        await client.AnswerAsync(question.Id, new ConsoleAnswer(Continue: true), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Done, (await pausing.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken)).Outcome);
        Assert.Contains("Someone continued the run at this machine.", _console.ToString(), StringComparison.Ordinal);
    }

    // A stop ends the wait as a stop, which leaves the step running for the run to go on with after a restart.
    [Fact]
    public async Task AStopEndsTheWait()
    {
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        ScriptedMachineConsole console = new(_ =>
        {
            stop.Cancel();

            return new ConsoleAnswer(Back: true);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(console, cancellationToken: stop.Token));

        Assert.Equal(RunActivity.Step, _heartbeat.Activity);
    }

    [Fact]
    public async Task AMessageThatCannotBeWorkedOutFailsTheStep()
    {
        StepResult result = await RunAsync(new ScriptedMachineConsole(), s_pause with { Message = "Call {{Technician}}." });

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.StartsWith("The pause's message cannot be worked out: ", result.Error, StringComparison.Ordinal);
        Assert.Empty(_server.RunReports);
    }

    private Task<StepResult> RunAsync(IMachineConsole console, PauseStep? step = null, CancellationToken? cancellationToken = null)
    {
        StepContext context = new(
            TestRuns.RunId,
            SequencePhase.WindowsPE,
            new MachineVariables("Dell Inc.", "Latitude 5440", "SN-1", "4c4c4544-0042-3510-8052-b4c04f4d3232", ["00155D010203"], "PC-042", SequencePhase.WindowsPE),
            new Dictionary<string, string>(),
            new RecordingProgress());

        return new PauseStepRunner(_heartbeat, console, _log, _time)
            .RunAsync(step ?? s_pause, context, cancellationToken ?? TestContext.Current.CancellationToken);
    }
}
