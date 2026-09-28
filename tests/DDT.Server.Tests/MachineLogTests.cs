// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// The server's clock stands still here, so the time a batch arrives is exactly the clock's.
public sealed class MachineLogTests(ManualClockApplication application) : IClassFixture<ManualClockApplication>
{
    private static AgentLogLine Line(DateTimeOffset time, string message, Guid? stepId = null) =>
        new(time, AgentLogLevel.Information, message, stepId);

    private async Task<MachineLogPage> ReadAsync(Guid machineId, string query = "") =>
        await RegisteredMachine.ReadAsync<MachineLogPage>(await (await application.AdministratorAsync()).GetAsync($"/api/machines/{machineId}/log{query}"));

    private static async Task LogAsync(DeployingMachine machine, AgentLogBatch batch) =>
        Assert.Equal(HttpStatusCode.NoContent, (await machine.Agent.LogAsync(machine.Id, machine.Token, batch)).StatusCode);

    [Fact]
    public async Task PagesBackFromTheNewestLinesAndCatchesUp()
    {
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, await application.AdministratorAsync());
        DateTimeOffset now = application.Clock.GetUtcNow();
        await LogAsync(machine, new AgentLogBatch([.. Enumerable.Range(0, 12).Select(i => Line(now, $"line {i}"))]));

        MachineLogPage newest = await ReadAsync(machine.Id, "?limit=5");
        Assert.Equal(["line 7", "line 8", "line 9", "line 10", "line 11"], newest.Lines.Select(l => l.Message));
        Assert.True(newest.HasOlder);

        MachineLogPage older = await ReadAsync(machine.Id, $"?limit=5&before={newest.Lines[0].Id}");
        Assert.Equal(["line 2", "line 3", "line 4", "line 5", "line 6"], older.Lines.Select(l => l.Message));
        Assert.True(older.HasOlder);

        MachineLogPage oldest = await ReadAsync(machine.Id, $"?limit=5&before={older.Lines[0].Id}");
        Assert.Equal(["line 0", "line 1"], oldest.Lines.Select(l => l.Message));
        Assert.False(oldest.HasOlder);

        MachineLogPage caughtUp = await ReadAsync(machine.Id, $"?limit=3&after={older.Lines[^1].Id}");
        Assert.Equal(["line 7", "line 8", "line 9"], caughtUp.Lines.Select(l => l.Message));
        Assert.True(caughtUp.HasOlder);

        MachineLogPage nothingNew = await ReadAsync(machine.Id, $"?after={newest.Lines[^1].Id}");
        Assert.Empty(nothingNew.Lines);
        Assert.True(nothingNew.HasOlder);

        Assert.Single((await ReadAsync(machine.Id, "?limit=0")).Lines);
        Assert.Equal(12, (await ReadAsync(machine.Id)).Lines.Count);

        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/machines/{machine.Id}/log")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/machines/{Guid.NewGuid()}/log")).StatusCode);
    }

    // The Windows PE clock can be hours off. The batch says when the agent sent it by that clock, which corrects it.
    [Fact]
    public async Task CorrectsAClockThatIsOff()
    {
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, await application.AdministratorAsync());
        DateTimeOffset received = application.Clock.GetUtcNow();
        DateTimeOffset agentNow = received - TimeSpan.FromHours(3);

        await LogAsync(machine, new AgentLogBatch([Line(agentNow - TimeSpan.FromSeconds(10), "behind"), Line(agentNow + TimeSpan.FromHours(4), "ahead")], agentNow));
        await LogAsync(machine, new AgentLogBatch([Line(received - TimeSpan.FromSeconds(5), "network")], received - TimeSpan.FromSeconds(1.5)));
        await LogAsync(machine, new AgentLogBatch([Line(received - TimeSpan.FromSeconds(5), "tolerance")], received - TimeSpan.FromSeconds(2)));
        await LogAsync(machine, new AgentLogBatch([Line(agentNow, "old agent")]));
        await LogAsync(machine, new AgentLogBatch([Line(new DateTimeOffset(received.UtcDateTime).ToOffset(TimeSpan.FromHours(2)) - TimeSpan.FromMinutes(1), "offset")]));

        Dictionary<string, MachineLogEntry> lines = (await ReadAsync(machine.Id)).Lines.ToDictionary(l => l.Message);

        Assert.Equal(received - TimeSpan.FromSeconds(10), lines["behind"].TimestampUtc);
        Assert.Equal(agentNow - TimeSpan.FromSeconds(10), lines["behind"].AgentTimestampUtc);

        // Never after the moment it arrived.
        Assert.Equal(received, lines["ahead"].TimestampUtc);

        // Below the tolerance, the difference is the network's delay. From the tolerance on, it is the clock's.
        Assert.Equal(received - TimeSpan.FromSeconds(5), lines["network"].TimestampUtc);
        Assert.Equal(received - TimeSpan.FromSeconds(3), lines["tolerance"].TimestampUtc);

        // An agent that does not say when it sent the batch keeps its own times.
        Assert.Equal(agentNow, lines["old agent"].TimestampUtc);

        Assert.All(lines.Values, line =>
        {
            Assert.Equal(TimeSpan.Zero, line.TimestampUtc.Offset);
            Assert.Equal(TimeSpan.Zero, line.AgentTimestampUtc.Offset);
            Assert.Equal(received, line.ReceivedUtc);
        });
        Assert.Equal(received - TimeSpan.FromMinutes(1), lines["offset"].TimestampUtc);
    }

    [Fact]
    public async Task TagsTheLinesOfARunWithTheRunAndTheStep()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        DateTimeOffset now = application.Clock.GetUtcNow();

        await LogAsync(machine, new AgentLogBatch([Line(now, "before")]));

        await administrator.AssignedAsync(machine.Id, sequence.Id);
        AgentRun run = (await machine.NextAsync()).Run!;
        Guid step = run.Sequence.Steps[0].Id;
        await machine.ReportOkAsync(run.Id, Running(Step(run.Sequence.Steps[0], StepState.Running)));
        await LogAsync(machine, new AgentLogBatch([Line(now, "during", step), Line(now, "between")]));
        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Failed, [Step(run.Sequence.Steps[0], StepState.Failed, "Exit code 1.")]) with { Error = "Exit code 1." });

        // A run that ended claims no more lines, whatever step the agent names.
        await LogAsync(machine, new AgentLogBatch([Line(now, "after", step)]));

        MachineLogPage all = await ReadAsync(machine.Id);
        Assert.Equal(["before", "during", "between", "after"], all.Lines.Select(l => l.Message));
        Assert.Equal([null, run.Id, run.Id, null], all.Lines.Select(l => l.DeploymentId));
        Assert.Equal([null, step, null, null], all.Lines.Select(l => l.StepId));

        MachineLogPage ofTheRun = await ReadAsync(machine.Id, $"?deploymentId={run.Id}");
        Assert.Equal(["during", "between"], ofTheRun.Lines.Select(l => l.Message));
        Assert.False(ofTheRun.HasOlder);
    }
}
