// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Threading.Channels;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Live;
using Microsoft.AspNetCore.SignalR;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// A page that shows a machine watches it, and only such a page receives its log lines and step changes.
public sealed class LiveWatchTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static AgentLogBatch Lines(params string[] messages) =>
        new([.. messages.Select(message => new AgentLogLine(DateTimeOffset.UtcNow, AgentLogLevel.Information, message))]);

    private async Task<long> LastLineIdAsync(Guid machineId) =>
        (await RegisteredMachine.ReadAsync<MachineLogPage>(await (await application.AdministratorAsync()).GetAsync($"/api/machines/{machineId}/log?limit=1"))).Lines[^1].Id;

    [Fact]
    public async Task OnlyTheConnectionsThatWatchAMachineGetItsLogAndItsSteps()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener watcher = await LiveListener.StartAsync(application, administrator);
        await using LiveListener bystander = await LiveListener.StartAsync(application, administrator);
        ChannelReader<MachineLogAppendedEvent> logs = watcher.Listen<MachineLogAppendedEvent>(LiveEvents.MachineLogAppended);
        ChannelReader<RunStepChangedEvent> steps = watcher.Listen<RunStepChangedEvent>(LiveEvents.RunStepChanged);
        ChannelReader<MachineLogAppendedEvent> otherLogs = bystander.Listen<MachineLogAppendedEvent>(LiveEvents.MachineLogAppended);
        ChannelReader<RunStepChangedEvent> otherSteps = bystander.Listen<RunStepChangedEvent>(LiveEvents.RunStepChanged);
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());

        await administrator.AssignedAsync(machine.Id, sequence.Id);
        AgentRun run = (await machine.NextAsync()).Run!;
        await watcher.WatchAsync(machine.Id);

        (await machine.Agent.LogAsync(machine.Id, machine.Token, Lines("first", "second"))).EnsureSuccessStatusCode();
        MachineLogAppendedEvent appended = await LiveListener.NextAsync(logs);

        Assert.Equal(machine.Id, appended.MachineId);
        Assert.Equal(await LastLineIdAsync(machine.Id), appended.LastLineId);

        // A second batch within the same second arrives when that second ends, as one push with the newest line.
        (await machine.Agent.LogAsync(machine.Id, machine.Token, Lines("third"))).EnsureSuccessStatusCode();
        (await machine.Agent.LogAsync(machine.Id, machine.Token, Lines("fourth"))).EnsureSuccessStatusCode();
        long newest = await LastLineIdAsync(machine.Id);

        Assert.Equal(machine.Id, (await LiveListener.NextAsync(logs, e => e.LastLineId == newest)).MachineId);

        await machine.ReportOkAsync(run.Id, Running(Step(run.Sequence.Steps[0], StepState.Running)));
        RunStepChangedEvent changed = await LiveListener.NextAsync(steps);

        Assert.Equal(machine.Id, changed.MachineId);
        Assert.Equal(run.Id, changed.DeploymentId);
        Assert.Equal(run.Sequence.Steps[0].Id, changed.Step.StepId);
        Assert.Equal(StepState.Running, changed.Step.State);

        Assert.False(otherLogs.TryRead(out _));
        Assert.False(otherSteps.TryRead(out _));

        // After unwatching, nothing more arrives. Not even the push that was waiting for its second to end.
        await watcher.UnwatchAsync(machine.Id);
        (await machine.Agent.LogAsync(machine.Id, machine.Token, Lines("fifth"))).EnsureSuccessStatusCode();
        await Task.Delay(LiveNotifier.LogPushInterval * 2, TestContext.Current.CancellationToken);

        Assert.False(logs.TryRead(out _));
    }

    [Fact]
    public async Task AConnectionWatchesAtMostSixteenMachines()
    {
        await using LiveListener listener = await LiveListener.StartAsync(application, await application.AdministratorAsync());
        Guid[] machines = [.. Enumerable.Range(0, LiveHub.MaxWatchedMachines).Select(_ => Guid.NewGuid())];

        foreach (Guid machine in machines)
        {
            await listener.WatchAsync(machine);
        }

        // Watching one of them again doesn't use up a slot.
        await listener.WatchAsync(machines[0]);

        HubException refused = await Assert.ThrowsAsync<HubException>(() => listener.WatchAsync(Guid.NewGuid()));
        Assert.Contains($"A connection watches at most {LiveHub.MaxWatchedMachines} machines.", refused.Message, StringComparison.Ordinal);

        await listener.UnwatchAsync(machines[0]);
        await listener.WatchAsync(Guid.NewGuid());
    }
}
