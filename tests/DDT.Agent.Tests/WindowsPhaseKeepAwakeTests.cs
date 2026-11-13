// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsPhase;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// A long step must not end in sleep, so the machine stays awake while the run goes on in Windows.
public sealed class WindowsPhaseKeepAwakeTests : WindowsPhaseLoopTestBase
{
    [Fact]
    public async Task KeepsTheMachineAwakeWhileTheStepsRunAndLetsItSleepBeforeTheAgentLeaves()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        FakeKeepAwake awake = new(_tools);
        List<int> heldInSteps = [];
        _toolRunner.AnswerExitCode = (_, _, _) =>
        {
            heldInSteps.Add(awake.Held);

            return 0;
        };
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run))
            .OnRunReport(DeploymentState.Done, _ => new AgentRunReportResult("session-e", "resume-e", null));

        await RunAsync(server, new() { KeepAwake = awake });

        Assert.NotEmpty(heldInSteps);
        Assert.All(heldInSteps, held => Assert.Equal(1, held));
        Assert.Equal(0, awake.Held);
        Assert.InRange(_tools.Calls.IndexOf("may sleep"), 0, _tools.Calls.IndexOf("remove"));
    }

    // Asked for and given back at once, so this PC's power settings stay as they were.
    [Fact]
    public void TheRealPowerRequestIsSetAndCleared()
    {
        using StringWriter output = new();

        using (new PowerRequestKeepAwake(new AgentLog(new ImmediateTimeProvider(), output)).Hold())
        {
        }

        Assert.Equal("", output.ToString());
    }
}
