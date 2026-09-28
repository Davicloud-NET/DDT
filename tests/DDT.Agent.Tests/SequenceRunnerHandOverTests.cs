// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Agent.WindowsPhase;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// The hand-over of a run with steps in the installed Windows, and a Windows PE that starts again after it.
public sealed class SequenceRunnerHandOverTests : SequenceRunnerTestBase
{
    [Fact]
    public async Task HandsARunWithWindowsStepsOverToWindows()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRunReport(DeploymentState.Running, _ => new AgentRunReportResult("session-1", "resume-1", "run-token-1"));

        RunResult result = await RunAsync(server, InWindows());

        Assert.Equal(new RunResult(RunOutcome.Restarting), result);
        Assert.Equal(
            [
                "list", "prepare", "partition 0", "apply 1",
                "reg load", "reg query", .. Enumerable.Repeat("reg add", 8), "reg unload",
                "bcd", "firmware after the answer file", "reboot",
            ],
            _tools.Calls);

        string agent = Path.Combine(Windows, "DDT", WindowsHandOver.AgentDirectory);
        Assert.Equal(
            await File.ReadAllBytesAsync(TestAgents.AgentSource(_tools), cancellationToken),
            await File.ReadAllBytesAsync(Path.Combine(agent, "ddt-agent.exe"), cancellationToken));
        Assert.Equal(
            """{"serverUrl":"https://ddt.example:8443/","rootCertificate":"-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n"}""",
            await File.ReadAllTextAsync(Path.Combine(agent, "agent.json"), cancellationToken));

        // What the service reads when Windows starts: the rest of the run, in the Windows phase, and its token.
        LocalRun? staged = await LocalRun.LoadAsync(Windows, Log(), cancellationToken);
        Assert.NotNull(staged);
        Assert.Equal((SequencePhase.Windows, 3, "run-token-1"), (staged.State.Phase, staged.State.NextIndex, staged.RunToken));

        // Windows setup still needs the answer file.
        Assert.True(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.Equal(RestartInto.Windows, RestartDue());

        AgentRunReport last = server.RunReports[^1];
        Assert.Equal((DeploymentState.Running, SequencePhase.Windows, RunActivity.Restarting), (last.State, last.Phase, last.Activity));
    }

    [Fact]
    public async Task TheConsoleComesAlongAndSetupSignsInToDdtsSession()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string console = Path.Combine(_tools.Root, "X", "console");
        Directory.CreateDirectory(console);

        foreach (string file in ConsolePipe.Files)
        {
            await File.WriteAllTextAsync(Path.Combine(console, file), file, cancellationToken);
        }

        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRunReport(DeploymentState.Running, _ => new AgentRunReportResult("session-1", "resume-1", "run-token-1"));

        // A dry hand-over, as the console's directory would otherwise let only SYSTEM write to it.
        Assert.Equal(new RunResult(RunOutcome.Restarting), await RunAsync(server, InWindows(), new() { DryRunHandOver = true, ConsoleDirectory = console }));

        string staged = Path.Combine(Windows, "DDT", WindowsHandOver.ConsoleDirectory);
        Assert.Equal(ConsolePipe.Files.Order(), Directory.GetFiles(staged).Select(Path.GetFileName).Order());
        Assert.Contains("<Username>DDTDeploy</Username>", await File.ReadAllTextAsync(UnattendFile.PathIn(Windows), cancellationToken), StringComparison.Ordinal);
        Assert.Contains("\"password\"", await File.ReadAllTextAsync(DeploySession.FilePathIn(Windows), cancellationToken), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("reg add")]
    [InlineData("firmware")]
    public async Task AFailedHandOverForgetsTheRunAndLeavesNothingToStartWindowsWith(string failAt)
    {
        _tools.FailAt = failAt;
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        RunResult result = await RunAsync(server, InWindows());

        Assert.Equal(new RunResult(RunOutcome.Failed), result);
        Assert.Equal((DeploymentState.Failed, "The scripted step failed."), (server.RunReports[^1].State, server.RunReports[^1].Error));
        Assert.False(File.Exists(StatePath));
        Assert.False(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.DoesNotContain("reboot", _tools.Calls);

        if (failAt == "firmware")
        {
            Assert.Equal(["firmware after the answer file", "restore"], _tools.Calls[^2..]);
        }
        else
        {
            // The hive is unloaded, and the disk has no boot loader that could start Windows, which bcdboot may put
            // first in the firmware's boot order.
            Assert.Equal(["reg add", "reg unload"], _tools.Calls[^2..]);
            Assert.DoesNotContain("bcd", _tools.Calls);
        }
    }

    [Theory]
    [InlineData("reg load")]
    [InlineData("firmware")]
    public async Task AnInterruptedHandOverIsDoneAgainAtTheNextStart(string interruptedAt)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRunReport(DeploymentState.Running, _ => new AgentRunReportResult("session-1", "resume-1", "run-token-1"));

        // A stop there leaves on the disk what a power loss would.
        if (interruptedAt == "firmware")
        {
            _tools.PuttingWindowsFirst = server.Stop.Cancel;
        }
        else
        {
            Func<string, IReadOnlyList<string>, IReadOnlyList<string>> reg = _toolRunner.Answer!;
            _toolRunner.Answer = (fileName, arguments) =>
            {
                if (arguments[0] == "load" && !server.Stop.IsCancellationRequested)
                {
                    server.Stop.Cancel();
                    server.Stop.Token.ThrowIfCancellationRequested();
                }

                return reg(fileName, arguments);
            };
        }

        Assert.Equal(RunOutcome.Stopped, (await RunAsync(server, InWindows())).Outcome);

        // The service is registered before the state says the run goes on in Windows.
        LocalRun? found = await LocalRun.LoadAsync(Windows, Log(), cancellationToken);
        Assert.NotNull(found);
        Assert.Equal(interruptedAt == "firmware" ? SequencePhase.Windows : SequencePhase.WindowsPE, found.State.Phase);

        _tools.PuttingWindowsFirst = null;
        int callsBefore = _tools.Calls.Count;
        RunResult result = await RunAsync(_image.Serve(new ScriptedAgentServer()), InWindows() with { State = DeploymentState.Running }, new() { Resumed = found, RunToken = found.RunToken });

        Assert.Equal(RunOutcome.Restarting, result.Outcome);
        Assert.Equal(
            [
                $"find {FakeDeploymentTools.WindowsPartitionId}",
                "reg load", "reg query", .. Enumerable.Repeat("reg add", 8), "reg unload",
                "bcd", "firmware after the answer file", "reboot",
            ],
            _tools.Calls[callsBefore..]);

        // A Windows PE that finds the run in the Windows phase counts it, so firmware that always starts from the
        // network cannot hand the run over for ever.
        LocalRun? staged = await LocalRun.LoadAsync(Windows, Log(), cancellationToken);
        Assert.NotNull(staged);
        Assert.Equal((SequencePhase.Windows, 3, "run-token-1"), (staged.State.Phase, staged.State.NextIndex, staged.RunToken));
        Assert.Equal(interruptedAt == "firmware" ? "1" : null, staged.State.Variables.GetValueOrDefault(RunVariables.WindowsPEReturns));
    }

    [Fact]
    public async Task ARunWhoseWindowsNeverStartsFailsAfterItWasHandedOverThreeTimes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        AgentLog log = Log();
        AgentRun run = InWindows() with { State = DeploymentState.Running };
        SequenceState state = SequenceStates.Start(run.Id, run.Sequence) with
        {
            Phase = SequencePhase.Windows,
            NextIndex = 3,
            Steps = [.. run.Sequence.Steps.Select((step, index) => new StepRunState(step.Id, index < 3 ? StepState.Done : StepState.Pending, null))],
            Variables = new Dictionary<string, string>(RunVariables.Of(_tools.Volumes)) { [RunVariables.WindowsPEReturns] = "3" },
        };
        RunFiles files = RunFiles.In(Windows, log);
        await files.SaveStateAsync(state, cancellationToken);
        await files.SaveTokenAsync("run-token-1", cancellationToken);
        await UnattendFile.WriteAsync(Windows, TestImage.Unattend, cancellationToken);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        RunResult result = await RunAsync(server, run, new() { Resumed = await LocalRun.LoadAsync(Windows, log, cancellationToken), RunToken = "run-token-1" });

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal((DeploymentState.Failed, SequenceRunner.WindowsDidNotStartMessage), (server.RunReports[^1].State, server.RunReports[^1].Error));
        Assert.Equal([$"find {FakeDeploymentTools.WindowsPartitionId}"], _tools.Calls);
        Assert.False(File.Exists(StatePath));
        Assert.False(File.Exists(files.TokenPath));
        Assert.False(File.Exists(UnattendFile.PathIn(Windows)));
    }

    [Theory]
    [InlineData("report")]
    [InlineData("log")]
    public async Task ARefusedTokenAfterTheHandOverDoesNotStartWindows(string refused)
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        if (refused == "report")
        {
            server.AnswerRunReports = (report, token) => report.Activity == RunActivity.Restarting
                ? throw new AgentTokenRejectedException()
                : new AgentRunReportResult(token, "resume", null);
        }
        else
        {
            server.AnswerLogs = batch =>
            {
                if (batch.Lines.Any(line => line.Message.StartsWith("The machine restarts into the installed Windows", StringComparison.Ordinal)))
                {
                    throw new AgentTokenRejectedException();
                }
            };
        }

        RunResult result = await RunAsync(server, InWindows());

        // Most likely the run was stopped. The registration with the run token decides, so the state and the answer
        // file stay, but Windows must not start and go on with the run meanwhile.
        Assert.Equal(RunOutcome.TokenRejected, result.Outcome);
        Assert.Equal(SequenceRunner.LostContactMessage, result.UnsentReport?.Error);
        Assert.Equal(["firmware after the answer file", "restore"], _tools.Calls[^2..]);
        Assert.Null(RestartDue());
        Assert.True(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.Equal(SequencePhase.Windows, (await LoadStateAsync())?.Phase);
    }

    [Fact]
    public async Task ADryRunHandOverStagesTheAgentAndOnlyLogsTheRegistration()
    {
        StringWriter console = new();
        ImmediateTimeProvider time = new();
        AgentLog log = new(time, console);

        RunResult result = await RunAsync(_image.Serve(new ScriptedAgentServer()), InWindows(), new() { Time = time, Log = log, ToolRunner = new DryRunToolRunner(log), DryRunHandOver = true });

        Assert.Equal(RunOutcome.Restarting, result.Outcome);
        Assert.True(File.Exists(Path.Combine(Windows, "DDT", WindowsHandOver.AgentDirectory, "ddt-agent.exe")));
        Assert.Equal(SequencePhase.Windows, (await LoadStateAsync())?.Phase);

        string[] lines = console.ToString().Split(Environment.NewLine);
        string reg = OfflineServiceRegistration.RegPath;
        string load = ToolRunner.CommandLine(reg, ["load", OfflineServiceRegistration.HiveKey, OfflineServiceRegistration.HivePathIn(Windows)]);
        string add = ToolRunner.CommandLine(reg, ["add", $@"{OfflineServiceRegistration.HiveKey}\ControlSet001\Services\DdtSequence", "/v", "ImagePath"]);
        Assert.Contains(lines, line => line.EndsWith($"Dry run: not run: {load}", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains($"Dry run: not run: {add}", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EndsWith($"Dry run: not run: {ToolRunner.CommandLine(reg, ["unload", OfflineServiceRegistration.HiveKey])}", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("inherits the run directory's DACL (D:P(A;OICI;FA;;;SY))", StringComparison.Ordinal));
    }
}
