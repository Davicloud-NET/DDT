// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// The deployment record a run leaves in Windows as it ends there.
public sealed class WindowsPhaseRecordTests : WindowsPhaseLoopTestBase
{
    private static readonly Guid s_sequenceId = Guid.Parse("0193a4b2-0000-7000-8000-0000000000c1");

    private AgentRun RecordedRun() => Run(TestRuns.Script(4, SequencePhase.Windows)) with
    {
        SequenceId = s_sequenceId,
        SequenceRevision = 12,
        ImageName = "Windows 11 Pro",
        ServerName = "ddt.corp.example",
    };

    private async Task<(FakeDeploymentRecord Record, int ExitCode)> EndAsync(AgentRun run, DeploymentState state, Exception? failure = null)
    {
        _toolRunner.AnswerExitCode = (_, _, _) => state == DeploymentState.Done ? 0 : 1;
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run))
            .OnRunReport(state, _ => new AgentRunReportResult("session-e", "resume-e", null));
        FakeDeploymentRecord record = new(_tools) { Failure = failure };

        int exitCode = await RunAsync(server, new() { AutomaticRepair = new FakeAutomaticRepair(_tools), DeploymentRecord = record });

        return (record, exitCode);
    }

    [Theory]
    [InlineData(DeploymentState.Done)]
    [InlineData(DeploymentState.Failed)]
    public async Task WritesTheRecordAfterAutomaticRepairIsBackAndBeforeTheAgentLeaves(DeploymentState state)
    {
        await EndAsync(RecordedRun(), state);

        string[] calls = [.. _tools.Calls.Where(call => call != "reboot")];
        Assert.Equal(["automatic repair back", $"deployment record {state}", "remove"], calls[^3..]);
    }

    [Fact]
    public async Task TheRecordSaysWhatDeployedTheMachine()
    {
        AgentRun run = RecordedRun();

        (FakeDeploymentRecord record, _) = await EndAsync(run, DeploymentState.Done);

        DeploymentRecordEntry entry = Assert.Single(record.Entries);
        Assert.Equal(
            (run.SequenceName, (Guid?)s_sequenceId, (long?)12, run.Id, "Done", "Windows 11 Pro", TestAgents.Version, "ddt.corp.example"),
            (entry.SequenceName, entry.SequenceId, entry.SequenceRevision, entry.RunId, entry.Result, entry.ImageName, entry.DdtVersion, entry.ServerName));
    }

    [Fact]
    public async Task TheServerCanLeaveTheRecordOut()
    {
        await EndAsync(RecordedRun() with { SkipDeploymentRecord = true }, DeploymentState.Done);

        Assert.DoesNotContain(_tools.Calls, call => call.StartsWith("deployment record", StringComparison.Ordinal));
        Assert.Contains("remove", _tools.Calls);
    }

    [Fact]
    public async Task ARecordThatCannotBeWrittenNeverFailsTheRun()
    {
        (_, int exitCode) = await EndAsync(RecordedRun(), DeploymentState.Done, new UnauthorizedAccessException("Access is denied."));

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Contains("remove", _tools.Calls);
    }

    [Fact]
    public async Task AServiceWithoutARunWritesNoRecord()
    {
        FakeDeploymentRecord record = new(_tools);

        await RunAsync(new ScriptedAgentServer(), new() { DeploymentRecord = record });

        Assert.Equal(["remove"], _tools.Calls);
    }
}
