// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Consoles;
using DDT.ConsoleProtocol;
using DDT.Agent.Sequences;
using DDT.Agent.WindowsPhase;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// How the service goes on with the run, waits for Windows setup, and removes the agent and DDT's session once it is over.
public sealed class WindowsPhaseLoopTests : WindowsPhaseLoopTestBase
{
    [Fact]
    public async Task GoesOnWithTheRunAndRemovesItselfAfterTheDoneReportThenRestartsWindows()
    {
        RunScriptStep cmd = TestRuns.Script(4, SequencePhase.Windows);
        RunScriptStep powerShell = TestRuns.Script(5, SequencePhase.Windows, ScriptInterpreter.PowerShell);
        AgentRun run = Run(cmd, powerShell);
        await HandOverAsync(run);

        List<(bool AnswerFile, string? WindowsPEReturns)> found = NoteWhatEachScriptFinds();

        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run))
            .OnRunReport(DeploymentState.Done, _ =>
            {
                _tools.Note("done report");

                return new AgentRunReportResult("session-d", "resume-d", null);
            });

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(["setup finished", "run cmd.exe", "run cmd.exe", "done report", "remove", "reboot"], _tools.Calls);

        AgentRegistration registration = Assert.Single(server.Registrations);
        Assert.Equal(
            ("run-token-1", AgentEnvironment.Windows, SequenceDefinition.CurrentVersion, null, null),
            (registration.RunToken, registration.Environment, registration.SequenceVersion, registration.ResumeToken, registration.Disks));

        // The installed Windows reports the facts as Windows PE did, as conditions test them in both phases.
        Assert.NotNull(registration.Facts);
        Assert.Equal(new DryRunMachineIdentityReader(1).Read().Facts, registration.Facts);

        // The installed Windows' own tools run the scripts, which wait in the run's directory on its volume.
        string scripts = Path.Combine(Windows, "DDT", "scripts");
        Assert.Equal(
            [
                RecordingToolRunner.CommandLine(RunScriptStepRunner.CmdPath, "/d", "/c", Path.Combine(scripts, $"{cmd.Id:D}.cmd")),
                RecordingToolRunner.CommandLine(RunScriptStepRunner.CmdPath, "/d", "/c", Path.Combine(scripts, $"{powerShell.Id:D}.cmd")),
            ],
            _toolRunner.Calls);
        Assert.StartsWith(Environment.SystemDirectory, RunScriptStepRunner.CmdPath, StringComparison.OrdinalIgnoreCase);

        // The answer file went before the first step, and the count of returns to Windows PE stays with the run.
        Assert.Equal([(false, "1"), (false, "1")], found);

        Assert.All(server.RunReports, report => Assert.Equal(SequencePhase.Windows, report.Phase));
        AgentRunReport done = server.RunReports[^1];
        Assert.Equal(DeploymentState.Done, done.State);
        Assert.Equal(Enumerable.Repeat(StepState.Done, 5), done.Steps.Select(step => step.State));
        Assert.False(File.Exists(RunFiles.StatePathIn(Windows)));
        Assert.False(File.Exists(RunFiles.In(Windows, Log()).TokenPath));
    }

    // With the agent's own removal, and the agent staged as the hand-over leaves it.
    [Fact]
    public async Task AFinishedRunLeavesNothingOfDdtOnceWindowsStartsAgain()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        string agent = Path.Combine(Windows, "DDT", WindowsHandOver.AgentDirectory);
        Directory.CreateDirectory(agent);
        File.WriteAllBytes(Path.Combine(agent, WindowsHandOver.AgentFileName), [0x4D, 0x5A]);
        File.WriteAllText(Path.Combine(agent, WindowsHandOver.ConfigurationFileName), "{}");

        _toolRunner.Answer = (fileName, arguments) =>
        {
            _tools.Note($"run {Path.GetFileName(fileName)} {string.Join(' ', arguments)}");

            return [];
        };
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run))
            .OnRunReport(DeploymentState.Done, _ =>
            {
                _tools.Note("done report");

                return new AgentRunReportResult("session-d", "resume-d", null);
            });
        AgentLog log = Log();

        int exitCode = await RunAsync(server, new() { Log = log, Removal = new AgentRemoval(Windows, _toolRunner, _tools, log) });

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(
            [
                "setup finished",
                "done report",
                "run sc.exe delete DdtSequence",
                @"delete at restart DDT\agent\agent.json",
                @"delete at restart DDT\agent\ddt-agent.exe",
                @"delete at restart DDT\agent",
                @"delete at restart DDT",
                "reboot",
            ],
            _tools.Calls);

        _tools.DeleteMarkedAsWindowsStarts();

        Assert.False(Directory.Exists(Path.Combine(Windows, "DDT")));
    }

    [Fact]
    public async Task AStopAfterTheDoneReportStillRemovesTheAgentButDoesNotRestartWindows()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));
        server.OnRunReport(DeploymentState.Done, _ =>
        {
            server.Stop.Cancel();

            return new AgentRunReportResult("session-d", "resume-d", null);
        });

        Assert.Equal(AgentExitCodes.Deployed, await RunAsync(server));
        Assert.Equal(["setup finished", "remove"], _tools.Calls);
    }

    [Theory]
    [InlineData("nothing to continue")]
    [InlineData("no run")]
    [InlineData("another run")]
    [InlineData("rejected")]
    [InlineData("next without the run")]
    [InlineData("next with another run")]
    public async Task RemovesItselfWhenTheServerNoLongerRunsTheRun(string answer)
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new();

        _ = answer switch
        {
            "nothing to continue" => server.OnRegister(_ => throw new AgentRequestException(
                "The server answered 409.",
                "This machine has no run for the DDT service to continue. The service removes itself.",
                HttpStatusCode.Conflict)),
            "no run" => server.OnRegister(_ => Continued() with { RunId = null, RunToken = null }),
            "another run" => server.OnRegister(_ => Continued() with { RunId = Guid.NewGuid() }),
            "rejected" => server.OnRegister(_ => Continued() with { State = MachineState.Rejected, Token = null, ResumeToken = null }),
            "next without the run" => server.OnRegister(_ => Continued()).OnNext(_ => Next("session-1", null)),
            _ => server.OnRegister(_ => Continued()).OnNext(_ => Next("session-1", run with { Id = Guid.NewGuid() })),
        };

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Equal(["remove"], _tools.Calls);
        Assert.Empty(_toolRunner.Calls);
        Assert.Empty(server.RunReports);
        Assert.False(File.Exists(RunFiles.StatePathIn(Windows)));
        Assert.False(File.Exists(RunFiles.In(Windows, Log()).TokenPath));
        Assert.False(File.Exists(AnswerFile));
    }

    // The network may still be coming up while Windows starts, or a proxy in front of the server may answer for it:
    // only the server's own answer that the run is over ends it here, as the agent's removal cannot be undone. A
    // registration the server refuses as invalid is tried again as well.
    [Theory]
    [InlineData(null)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ARegistrationThatFailsIsTriedAgainAndTheRunGoesOn(HttpStatusCode? status)
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => throw (status is { } code
                ? new AgentRequestException($"The server answered {(int)code}.", "The server cannot take the registration now.", code)
                : new HttpRequestException("No such host is known.")))
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(2, server.Registrations.Count);
        Assert.Single(_toolRunner.Calls);
        Assert.Equal(DeploymentState.Done, server.RunReports[^1].State);
    }

    [Fact]
    public async Task RemovesItselfWhenNoRunWaits()
    {
        ScriptedAgentServer server = new();

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Equal(["remove"], _tools.Calls);
        Assert.Empty(server.Registrations);
    }

    [Fact]
    public async Task LeavesARunThatStillGoesOnInWindowsPEAlone()
    {
        await HandOverAsync(Run(TestRuns.Script(4, SequencePhase.Windows)), SequencePhase.WindowsPE);
        ScriptedAgentServer server = new();

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Empty(_tools.Calls);
        Assert.Empty(server.Registrations);
        Assert.True(File.Exists(RunFiles.StatePathIn(Windows)));
    }

    [Fact]
    public async Task WaitsForWindowsSetupAndSaysSo()
    {
        _tools.SetupRuns("Windows setup is still running", "the out-of-box experience is still running");
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        bool? answerFileWhileWaiting = null;
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));
        server.AnswerRunReports = (report, token) =>
        {
            if (report.Activity == RunActivity.WaitingForWindowsSetup)
            {
                answerFileWhileWaiting ??= File.Exists(AnswerFile);
            }

            return new AgentRunReportResult(token, "resume", "run-token-3");
        };

        ManualTimeProvider time = new();
        Task<int> running = RunAsync(server, new() { Time = time });
        await time.AdvanceUntilAsync(WindowsPhaseLoop.SetupPollInterval, () => running.IsCompleted);

        Assert.Equal(AgentExitCodes.Deployed, await running);
        Assert.Equal(
            ["setup Windows setup is still running", "setup the out-of-box experience is still running", "setup finished", "remove", "reboot"],
            _tools.Calls);

        // Setup still needed the answer file while the server was told what the machine waits for.
        Assert.True(answerFileWhileWaiting);
        AgentRunReport waiting = server.RunReports[0];
        Assert.Equal((DeploymentState.Running, SequencePhase.Windows, RunActivity.WaitingForWindowsSetup), (waiting.State, waiting.Phase, waiting.Activity));
        Assert.Equal(Enumerable.Repeat(StepState.Done, 3), waiting.Steps.Select(step => step.State));

        // Then the run went on. Its one quick step can be over before the heartbeat's first beat, which then says
        // Finishing already, as changes this close together share a beat.
        Assert.Contains(server.RunReports[1].Activity, (RunActivity[])[RunActivity.Step, RunActivity.Finishing]);
        Assert.False(File.Exists(AnswerFile));
    }

    // What the console of DDT's session shows while the service waits: the run, waiting for Windows setup.
    [Fact]
    public async Task TheConsoleShowsTheRunWaitingForWindowsSetup()
    {
        _tools.SetupRuns("Windows still sets up its first user");
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        List<ConsoleState> shown = [];
        ConsoleStatus status = TestAgents.Status(new RecordingConsole(shown));
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));

        ManualTimeProvider time = new();
        Task<int> running = RunAsync(server, new() { Time = time, Status = status });
        await time.AdvanceUntilAsync(WindowsPhaseLoop.SetupPollInterval, () => running.IsCompleted);

        Assert.Equal(AgentExitCodes.Deployed, await running);
        ConsoleState waiting = shown.First(state => state.Run is not null);
        Assert.Equal((ConsoleStage.Running, ConsoleActivity.WaitingForWindowsSetup), (waiting.Stage, waiting.Run!.Activity));
        Assert.Equal(s_machineId, waiting.MachineId);
        Assert.Equal(ConsoleStage.Finished, shown[^1].Stage);
    }

    [Fact]
    public async Task WarnsEveryHalfHourThatSetupHasNotFinished()
    {
        // Half an hour and a little more.
        _tools.SetupRuns([.. Enumerable.Repeat("the out-of-box experience is still running", 125)]);
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));

        ManualTimeProvider time = new();
        StringWriter console = new();
        Task<int> running = RunAsync(server, new() { Time = time, Log = new AgentLog(time, console) });
        await time.AdvanceUntilAsync(WindowsPhaseLoop.SetupPollInterval, () => running.IsCompleted);

        Assert.Equal(AgentExitCodes.Deployed, await running);
        string warning = Assert.Single(console.ToString().Split(Environment.NewLine), line => line.Contains("WARN", StringComparison.Ordinal));
        Assert.EndsWith(
            "Windows setup has not finished after 30 min 0 s: the out-of-box experience is still running. If it waits for someone at the machine, finish it there.",
            warning,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStopWhileSetupRunsKeepsTheRunForTheNextStart()
    {
        _tools.SetupRuns([.. Enumerable.Repeat("Windows setup is still running", 10)]);
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));
        server.AnswerRunReports = (report, token) =>
        {
            server.Stop.Cancel();

            return new AgentRunReportResult(token, "resume", null);
        };

        int exitCode = await RunAsync(server, new() { Time = new ManualTimeProvider() });

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.DoesNotContain("remove", _tools.Calls);
        Assert.Empty(_toolRunner.Calls);
        Assert.True(File.Exists(RunFiles.StatePathIn(Windows)));
        Assert.True(File.Exists(AnswerFile));
    }

    [Fact]
    public async Task AFailedRunIsReportedBeforeTheAgentRemovesItself()
    {
        _toolRunner.AnswerExitCode = (_, _, _) => 1;
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run))
            .OnRunReport(DeploymentState.Failed, _ =>
            {
                _tools.Note("failed report");

                return new AgentRunReportResult("session-f", "resume-f", null);
            });

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Equal(["setup finished", "failed report", "remove"], _tools.Calls);
        Assert.StartsWith("The script ended with exit code 1", server.RunReports[^1].Error, StringComparison.Ordinal);
        Assert.False(File.Exists(RunFiles.StatePathIn(Windows)));
    }

    [Fact]
    public async Task PreparesDdtsSessionFirstAndSignsItOutBeforeTheAgentRemovesItself()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ =>
            {
                _tools.Note("register");

                return Continued();
            })
            .OnNext(_ => Next("session-1", run))
            .OnRunReport(DeploymentState.Done, _ => new AgentRunReportResult("session-d", "resume-d", null));

        int exitCode = await RunAsync(server, new() { Session = new FakeDeploySession(_tools) });

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(
            ["prepare the session", "register", "setup finished", "the session takes over the sign-in", "end the session, signing out", "remove", "reboot"],
            _tools.Calls);
    }

    [Fact]
    public async Task AfterAFailureTheSessionShowsItUntilSomeoneSignsOut()
    {
        _toolRunner.AnswerExitCode = (_, _, _) => 1;
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run))
            .OnRunReport(DeploymentState.Failed, _ => new AgentRunReportResult("session-f", "resume-f", null));

        int exitCode = await RunAsync(server, new() { Session = new FakeDeploySession(_tools) });

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Equal(
            ["prepare the session", "setup finished", "the session takes over the sign-in", "end the session once someone signed out", "remove"],
            _tools.Calls);
    }

    [Fact]
    public async Task AStopWhileTheSessionWaitsLeavesTheRemovalToTheNextStart()
    {
        _toolRunner.AnswerExitCode = (_, _, _) => 1;
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run))
            .OnRunReport(DeploymentState.Failed, _ => new AgentRunReportResult("session-f", "resume-f", null));

        int exitCode = await RunAsync(server, new() { Session = new FakeDeploySession(_tools, ends: false) });

        // The service stays, finds no run at its next start, and ends the session and itself then.
        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Equal(["prepare the session", "setup finished", "the session takes over the sign-in", "end the session once someone signed out"], _tools.Calls);
    }

    // Whether the answer file was there when each script ran, and how often Windows PE had started again by then.
    private List<(bool AnswerFile, string? WindowsPEReturns)> NoteWhatEachScriptFinds()
    {
        List<(bool AnswerFile, string? WindowsPEReturns)> found = [];
        _toolRunner.AnswerExitCode = (fileName, _, _) =>
        {
            _tools.Note($"run {Path.GetFileName(fileName)}");
            SequenceState? state = RunFiles.In(Windows, Log()).LoadStateAsync(CancellationToken.None).GetAwaiter().GetResult();
            found.Add((File.Exists(AnswerFile), state?.Variables.GetValueOrDefault(RunVariables.WindowsPEReturns)));

            return 0;
        };

        return found;
    }
}
