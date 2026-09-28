// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

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
public abstract class WindowsPhaseLoopTestBase : IDisposable
{
    private protected static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private protected readonly FakeDeploymentTools _tools = new();
    private protected readonly RecordingToolRunner _toolRunner = new();
    private protected readonly TestImage _image = new();

    public void Dispose() => _tools.Dispose();

    private protected string Windows => _tools.Volumes.Windows;

    private protected string AnswerFile => UnattendFile.PathIn(Windows);

    private protected static AgentLog Log() => new(new ImmediateTimeProvider(), TextWriter.Null);

    private protected int Restarts() => _tools.Calls.Count(call => call == "reboot");

    // For what happens on the loop's own thread, which a test cannot await.
    private protected static async Task WaitForAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private protected static AgentRegistrationResult Continued() =>
        new(s_machineId, MachineState.Deploying, "session-0", "resume-0", 10, null, TestRuns.RunId, "run-token-2");

    private protected static AgentNextResult Next(string token, AgentRun? run) =>
        new(MachineState.Deploying, token, "resume", 10, null, Run: run);

    // The Windows PE steps of InstallWindows, then these.
    private protected AgentRun Run(params SequenceStep[] inWindows) =>
        TestRuns.Run([.. TestRuns.InstallWindows, .. inWindows], _image, DeploymentState.Running);

    // As the hand-over leaves the run: the steps in Windows PE done, the answer file for setup, the run token, and one
    // start of Windows PE too many on the way.
    private protected async Task HandOverAsync(AgentRun run, SequencePhase phase = SequencePhase.Windows)
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

    private protected Task<int> RunAsync(ScriptedAgentServer server, ServiceRunOptions? options = null)
    {
        options ??= new ServiceRunOptions();
        TimeProvider time = options.Time ?? new ImmediateTimeProvider();
        AgentLog log = options.Log ?? new AgentLog(time, TextWriter.Null);
        SequenceRunner runner = TestAgents.Runner(
            server,
            _tools,
            log,
            time,
            new() { ToolRunner = _toolRunner, SystemDirectory = TestAgents.SystemDirectory(_tools), DryRun = false, Status = options.Status });

        WindowsPhaseLoop loop = new WindowsPhaseLoopBuilder
        {
            Server = server,
            IdentityReader = new DryRunMachineIdentityReader(1),
            Runner = runner,
            Setup = _tools,
            Rebooter = _tools,
            RestartMarker = _tools,
            Removal = options.Removal ?? _tools,
            Log = log,
            TimeProvider = time,
            HeartbeatInterval = Timeout.InfiniteTimeSpan,
            WindowsRoot = _tools.Volumes.Windows,
            AgentVersion = TestAgents.Version,
            DryRun = options.DryRun,
            Session = options.Session,
            Status = options.Status,
        }.Build();

        return loop.RunAsync(server.Stop.Token);
    }
}
