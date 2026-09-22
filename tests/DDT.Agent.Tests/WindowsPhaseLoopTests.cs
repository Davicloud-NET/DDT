// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Agent.WindowsPhase;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// The service in the installed Windows, from the state the hand-over leaves on its volume.
public sealed class WindowsPhaseLoopTests : IDisposable
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private readonly FakeDeploymentTools _tools = new();
    private readonly RecordingToolRunner _toolRunner = new();
    private readonly TestImage _image = new();

    public void Dispose() => _tools.Dispose();

    private string Windows => _tools.Volumes.Windows;

    private string AnswerFile => UnattendFile.PathIn(Windows);

    [Fact]
    public async Task GoesOnWithTheRunAndRemovesItselfAfterTheDoneReport()
    {
        RunScriptStep cmd = TestRuns.Script(4, SequencePhase.Windows);
        RunScriptStep powerShell = TestRuns.Script(5, SequencePhase.Windows, ScriptInterpreter.PowerShell);
        AgentRun run = Run(cmd, powerShell);
        await HandOverAsync(run);

        // What each script found when it ran.
        List<(bool AnswerFile, string? WindowsPEReturns)> found = [];
        _toolRunner.AnswerExitCode = (fileName, _, _) =>
        {
            _tools.Note($"run {Path.GetFileName(fileName)}");
            SequenceState? state = RunFiles.In(Windows, Log()).LoadStateAsync(CancellationToken.None).GetAwaiter().GetResult();
            found.Add((File.Exists(AnswerFile), state?.Variables.GetValueOrDefault(RunVariables.WindowsPEReturns)));

            return 0;
        };

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
        Assert.Equal(["setup finished", "run cmd.exe", "run powershell.exe", "done report", "remove"], _tools.Calls);

        AgentRegistration registration = Assert.Single(server.Registrations);
        Assert.Equal(
            ("run-token-1", AgentEnvironment.Windows, SequenceDefinition.CurrentVersion, null, null),
            (registration.RunToken, registration.Environment, registration.SequenceVersion, registration.ResumeToken, registration.Disks));

        // The installed Windows' own tools run the scripts, which wait in the run's directory on its volume.
        string scripts = Path.Combine(Windows, "DDT", "scripts");
        Assert.Equal(
            [
                RecordingToolRunner.CommandLine(RunScriptStepRunner.CmdPath, "/d", "/c", Path.Combine(scripts, $"{cmd.Id:D}.cmd")),
                RecordingToolRunner.CommandLine(
                    RunScriptStepRunner.PowerShellPath, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(scripts, $"{powerShell.Id:D}.ps1")),
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
        Task<int> running = RunAsync(server, time);
        await time.AdvanceUntilAsync(WindowsPhaseLoop.SetupPollInterval, () => running.IsCompleted);

        Assert.Equal(AgentExitCodes.Deployed, await running);
        Assert.Equal(
            ["setup Windows setup is still running", "setup the out-of-box experience is still running", "setup finished", "remove"],
            _tools.Calls);

        // Setup still needed the answer file while the server was told what the machine waits for.
        Assert.True(answerFileWhileWaiting);
        AgentRunReport waiting = server.RunReports[0];
        Assert.Equal((DeploymentState.Running, SequencePhase.Windows, RunActivity.WaitingForWindowsSetup), (waiting.State, waiting.Phase, waiting.Activity));
        Assert.Equal(Enumerable.Repeat(StepState.Done, 3), waiting.Steps.Select(step => step.State));
        Assert.Equal(RunActivity.Step, server.RunReports[1].Activity);
        Assert.False(File.Exists(AnswerFile));
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
        Task<int> running = RunAsync(server, time, new AgentLog(time, console));
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

        int exitCode = await RunAsync(server, new ManualTimeProvider());

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
    public async Task AFailureTheServerDidNotGetGoesOutAfterRegisteringAgain()
    {
        _toolRunner.AnswerExitCode = (_, _, _) => 1;
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run))
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-2", run));

        for (int attempt = 0; attempt <= ServerCallRules.MaxRetries; attempt++)
        {
            server.OnRunReport(DeploymentState.Failed, _ => throw new HttpRequestException("The server is restarting."));
        }

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Equal(["run-token-1", "run-token-2"], server.Registrations.Select(registration => registration.RunToken));
        Assert.Equal("run-report Failed session-2", server.Calls[^1]);
        Assert.StartsWith("The script ended with exit code 1", server.RunReports[^1].Error, StringComparison.Ordinal);
        Assert.Equal("remove", _tools.Calls[^1]);
    }

    [Fact]
    public async Task ARefusedTokenBeforeTheRunRegistersAgain()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => throw new AgentTokenRejectedException())
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-2", run));

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(2, server.Registrations.Count);
        Assert.Single(_toolRunner.Calls);
    }

    [Fact]
    public async Task AWindowsPEStepAfterTheWindowsStepsFailsTheRun()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows), TestRuns.Script(6));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Single(_toolRunner.Calls);
        Assert.Equal((DeploymentState.Failed, SequenceRunner.WindowsPEAfterWindowsMessage), (server.RunReports[^1].State, server.RunReports[^1].Error));
        Assert.Equal("remove", _tools.Calls[^1]);
    }

    [Fact]
    public async Task ARestartIsRecordedFirstAndWaitsForWindowsToStopTheService()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows), TestRuns.Reboot, TestRuns.Script(6, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));
        server.AnswerRunReports = (report, token) =>
        {
            if (report.Activity == RunActivity.Restarting)
            {
                _tools.Note("restarting report");
            }

            return new AgentRunReportResult(token, "resume", null);
        };

        // Five minutes never pass, so nothing asks for the restart again.
        Task<int> running = RunAsync(server, new ManualTimeProvider());
        await WaitForAsync(() => _tools.Calls.Contains("reboot"));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.False(running.IsCompleted);
        await server.Stop.CancelAsync();
        Assert.Equal(AgentExitCodes.Restarting, await running);
        Assert.Equal(["setup finished", "restart due", "restarting report", "reboot"], _tools.Calls);
        Assert.Equal(5, (await RunFiles.In(Windows, Log()).LoadStateAsync(TestContext.Current.CancellationToken))?.NextIndex);
    }

    // Stopped between the step that asked for the restart and the restart itself, the service starts again in the same
    // Windows, whose state says the step is done: it restarts Windows instead of going on.
    [Fact]
    public async Task AStopBeforeTheRestartRestartsWindowsAtTheNextStart()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows), TestRuns.Reboot, TestRuns.Script(6, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer first = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));
        first.AnswerRunReports = (report, token) =>
        {
            if (report.Activity == RunActivity.Restarting)
            {
                first.Stop.Cancel();
            }

            return new AgentRunReportResult(token, "resume", null);
        };

        Assert.Equal(AgentExitCodes.Stopped, await RunAsync(first));
        Assert.Equal(["setup finished", "restart due"], _tools.Calls);

        ScriptedAgentServer second = new();
        Task<int> running = RunAsync(second, new ManualTimeProvider());
        await WaitForAsync(() => _tools.Calls.Contains("reboot"));
        await second.Stop.CancelAsync();

        Assert.Equal(AgentExitCodes.Restarting, await running);
        Assert.Empty(second.Registrations);
        Assert.Equal(["setup finished", "restart due", "reboot"], _tools.Calls);
        Assert.Single(_toolRunner.Calls);
    }

    // The server may stop taking the machine's token once the step asked for the restart, when it is told of the
    // restart or sent the last log lines, which only the runner sends then. Unlike at the hand-over, Windows still
    // restarts before the next step runs.
    [Theory]
    [InlineData("the restart")]
    [InlineData("the last log lines")]
    public async Task ARefusedTokenAtTheRestartStillRestartsWindowsBeforeTheNextStep(string refused)
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows), TestRuns.Reboot, TestRuns.Script(6, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run))
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-2", run));

        if (refused == "the restart")
        {
            server.AnswerRunReports = (report, token) => report.Activity == RunActivity.Restarting
                ? throw new AgentTokenRejectedException()
                : new AgentRunReportResult(token, "resume", null);
        }
        else
        {
            server.AnswerLogs = batch =>
            {
                if (batch.Lines.Any(line => line.Message == "Windows restarts, and the run goes on after the restart."))
                {
                    throw new AgentTokenRejectedException();
                }
            };
        }

        Task<int> running = RunAsync(server, new ManualTimeProvider());
        await WaitForAsync(() => _tools.Calls.Contains("reboot"));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await server.Stop.CancelAsync();

        Assert.Equal(AgentExitCodes.Restarting, await running);
        Assert.Equal(["setup finished", "restart due", "reboot"], _tools.Calls);
        Assert.Single(_toolRunner.Calls);
    }

    // However the loop comes round again, a restart the run recorded comes first.
    [Fact]
    public async Task ARestartThatIsDueComesBeforeTheNextRegistration()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ =>
            {
                // As a run that recorded its restart and came back without it leaves the marker.
                _tools.RestartDue = true;

                throw new AgentTokenRejectedException();
            })
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-2", run));

        Task<int> running = RunAsync(server, new ManualTimeProvider());
        await WaitForAsync(() => _tools.Calls.Contains("reboot"));
        await server.Stop.CancelAsync();

        Assert.Equal(AgentExitCodes.Restarting, await running);
        Assert.Equal(["reboot"], _tools.Calls);
        Assert.Single(server.Registrations);
        Assert.Empty(_toolRunner.Calls);
    }

    [Fact]
    public async Task AsksForTheRestartAgainWhenWindowsHasNotRestartedAfterFiveMinutes()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows), TestRuns.Reboot);
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));
        ManualTimeProvider time = new();
        StringWriter console = new();

        Task<int> running = RunAsync(server, time, new AgentLog(time, console));

        // Then the loop's wait for the restart is the only timer.
        await WaitForAsync(() => Restarts() == 1 && time.PendingTimers == 1);
        time.Advance(WindowsPhaseLoop.RestartTimeout - TimeSpan.FromSeconds(1));

        Assert.Equal(1, Restarts());

        time.Advance(TimeSpan.FromSeconds(1));
        await WaitForAsync(() => Restarts() == 2);
        await server.Stop.CancelAsync();

        Assert.Equal(AgentExitCodes.Restarting, await running);
        Assert.Equal(2, Restarts());
        Assert.Contains(
            console.ToString().Split(Environment.NewLine),
            line => line.EndsWith("Windows has not restarted 5 minutes after the run asked it to. Asking again.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task JoinsTheDomainWithTheAccountOfTheRunningStepThenRestartsWindows()
    {
        AgentRun run = Run(TestRuns.Join, TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run))
            .OnRunCredentials(_ => TestRuns.JoinAccount);
        ManualTimeProvider time = new();
        StringWriter console = new();

        Task<int> running = RunAsync(server, time, new AgentLog(time, console));
        await WaitForAsync(() => _tools.Calls.Contains("reboot"));
        await server.Stop.CancelAsync();

        Assert.Equal(AgentExitCodes.Restarting, await running);
        Assert.Equal(["setup finished", "join corp.example.test", "restart due", "reboot"], _tools.Calls);
        Assert.Empty(_toolRunner.Calls);

        // The server hands out the account only for the step it has seen running.
        List<string> calls = server.Calls;
        int fetch = calls.IndexOf($"run-credentials {TestRuns.Join.Id} session-1");
        int reportsBefore = calls.Take(fetch).Count(call => call.StartsWith("run-report", StringComparison.Ordinal));
        Assert.Contains(new StepRunState(TestRuns.Join.Id, StepState.Running, null), server.RunReports[reportsBefore - 1].Steps);

        Assert.DoesNotContain(TestRuns.JoinAccount.Password, console.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(server.SentLines, line => line.Message.Contains(TestRuns.JoinAccount.Password, StringComparison.Ordinal));
        Assert.All(server.RunReports, report => Assert.DoesNotContain(TestRuns.JoinAccount.Password, report.Error ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADryRunReturnsAtTheRestart()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows), TestRuns.Reboot);
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));

        Assert.Equal(AgentExitCodes.Restarting, await RunAsync(server, dryRun: true));
        Assert.Equal(["setup finished", "restart due", "reboot"], _tools.Calls);
    }

    private static AgentLog Log() => new(new ImmediateTimeProvider(), TextWriter.Null);

    private int Restarts() => _tools.Calls.Count(call => call == "reboot");

    // For what happens on the loop's own thread, which a test cannot await.
    private static async Task WaitForAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static AgentRegistrationResult Continued() =>
        new(s_machineId, MachineState.Deploying, "session-0", "resume-0", 10, null, TestRuns.RunId, "run-token-2");

    private static AgentNextResult Next(string token, AgentRun? run) =>
        new(MachineState.Deploying, token, "resume", 10, null, Run: run);

    // The Windows PE steps of InstallWindows, then these.
    private AgentRun Run(params SequenceStep[] inWindows) =>
        TestRuns.Run([.. TestRuns.InstallWindows, .. inWindows], _image, DeploymentState.Running);

    // As the hand-over leaves the run: the steps in Windows PE done, the answer file for setup, the run token, and one
    // start of Windows PE too many on the way.
    private async Task HandOverAsync(AgentRun run, SequencePhase phase = SequencePhase.Windows)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SequenceState state = SequenceStates.Start(run.Id, run.Sequence) with
        {
            Phase = phase,
            NextIndex = 3,
            Steps = [.. run.Sequence.Steps.Select((step, index) => new StepRunState(step.Id, index < 3 ? StepState.Done : StepState.Pending, null))],
            Variables = new Dictionary<string, string>(RunVariables.Of(_tools.Volumes)) { [RunVariables.WindowsPEReturns] = "1" },
        };
        RunFiles files = RunFiles.In(Windows, Log());
        await files.SaveStateAsync(state, cancellationToken);
        await files.SaveTokenAsync("run-token-1", cancellationToken);
        await UnattendFile.WriteAsync(Windows, TestImage.Unattend, cancellationToken);
    }

    private Task<int> RunAsync(ScriptedAgentServer server, TimeProvider? time = null, AgentLog? log = null, bool dryRun = false)
    {
        time ??= new ImmediateTimeProvider();
        log ??= new AgentLog(time, TextWriter.Null);
        SequenceRunner runner = TestAgents.Runner(
            server,
            _tools,
            log,
            time,
            toolRunner: _toolRunner,
            systemDirectory: TestAgents.SystemDirectory(_tools),
            dryRun: false);

        return TestAgents.WindowsLoop(server, _tools, runner, log, time, dryRun).RunAsync(server.Stop.Token);
    }
}
