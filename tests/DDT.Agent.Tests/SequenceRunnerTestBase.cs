// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// One run in Windows PE with fakes for the disk and wimlib, the other tools, and the server.
public abstract class SequenceRunnerTestBase : IDisposable
{
    private protected static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");
    private protected static readonly MachineIdentity s_identity = new DryRunMachineIdentityReader(1).Read();

    private protected static readonly InjectDriversStep s_drivers = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000b005"), Name = "Drivers" };

    private protected readonly FakeDeploymentTools _tools = new();
    private protected readonly RecordingToolRunner _toolRunner = new();
    private protected readonly TestImage _image = new();
    private protected readonly string _system;
    private protected DeploymentTokens _tokens = new("session-0", "resume-0");

    protected SequenceRunnerTestBase()
    {
        _system = TestAgents.SystemDirectory(_tools);

        // reg.exe goes into the disk's journal, and the installed Windows uses its first control set.
        _toolRunner.Answer = (fileName, arguments) =>
        {
            if (Path.GetFileName(fileName) != "reg.exe")
            {
                return [];
            }

            _tools.Note($"reg {arguments[0]}");

            return arguments[0] == "query" ? [string.Empty, @"HKEY_LOCAL_MACHINE\DDT_OFFLINE\Select", "    Current    REG_DWORD    0x1", string.Empty] : [];
        };
    }

    public void Dispose() => _tools.Dispose();

    private protected string Windows => _tools.Volumes.Windows;

    private protected string StatePath => RunFiles.StatePathIn(Windows);

    private protected AgentRun InstallWindows(int? diskNumber = null) => TestRuns.Run(TestRuns.InstallWindows, _image, diskNumber: diskNumber);

    // Continues in Windows after the answer file.
    private protected AgentRun InWindows() => TestRuns.Run([.. TestRuns.InstallWindows, TestRuns.Script(4, SequencePhase.Windows)], _image);

    // The first beat is held back until the engine has asked for the restart and the runner recorded it. Then it runs
    // act, which may also refuse the beat. The partitioning waits until that beat reaches the server, however late.
    // Otherwise quick steps can finish before it, and the first report after them is the restart's.
    private protected void OnFirstBeatOnceTheRestartIsDue(ScriptedAgentServer server, Action act)
    {
        string marker = TestAgents.RestartMarker(_tools, Log()).FilePath;
        int beats = 0;
        TaskCompletionSource beating = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _tools.PartitionGate = beating;

        server.AnswerRunReports = (report, token) =>
        {
            if (report.Activity != RunActivity.Preparing && Interlocked.Increment(ref beats) == 1)
            {
                beating.TrySetResult();
                Assert.True(SpinWait.SpinUntil(() => File.Exists(marker), TimeSpan.FromSeconds(10)));
                act();
            }

            return new AgentRunReportResult(token, "resume", "run-token-1");
        };
    }

    private protected static AgentLog Log() => new(new ImmediateTimeProvider(), TextWriter.Null);

    private protected RestartInto? RestartDue() => TestAgents.RestartMarker(_tools, Log()).Due;

    // A console's lines without their time.
    private protected static string[] Lines(StringWriter console) =>
        [.. console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[9..])];

    private protected Task<SequenceState?> LoadStateAsync() => RunFiles.In(Windows, Log()).LoadStateAsync(TestContext.Current.CancellationToken);

    private protected Task<RunResult> RunAsync(ScriptedAgentServer server, AgentRun? run = null, SequenceRunOptions? options = null)
    {
        options ??= new SequenceRunOptions();
        TimeProvider time = options.Time ?? new ImmediateTimeProvider();
        _tokens = new DeploymentTokens("session-0", "resume-0", options.RunToken);
        SequenceRunner runner = TestAgents.Runner(
            server,
            _tools,
            options.Log ?? new AgentLog(time, TextWriter.Null),
            time,
            new()
            {
                HeartbeatInterval = options.HeartbeatInterval,
                ToolRunner = options.ToolRunner ?? _toolRunner,
                Rebooter = options.Rebooter,
                SystemDirectory = _system,
                DryRunHandOver = options.DryRunHandOver,
                ConsoleDirectory = options.ConsoleDirectory,
            });

        return runner.RunAsync(new RunRequest(s_machineId, run ?? InstallWindows(), options.Resumed, options.ConfirmedDisk, _tokens, s_identity), server.Stop.Token);
    }
}
