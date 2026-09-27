// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Threading.Channels;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Live;
using Xunit;

namespace DDT.Server.Tests;

// The server's clock stands still here, so every change a test makes falls within one push interval until the test
// advances the clock.
public sealed class MachinePushThrottleTests(ManualClockApplication application) : IClassFixture<ManualClockApplication>
{
    // Whether a push for the machine arrives within the wait.
    private static async Task<bool> PushedWithinAsync(ChannelReader<MachineSummary> pushes, Guid machineId, TimeSpan wait)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(wait);

        try
        {
            await foreach (MachineSummary pushed in pushes.ReadAllAsync(timeout.Token))
            {
                if (pushed.Id == machineId)
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
        }

        return false;
    }

    // A running sequence reports every few seconds, and every browser redraws the machine's row for each push. What
    // changes within a second after a push goes out as one push when the second ends, with the latest state.
    [Fact]
    public async Task ChangesWithinASecondGoOutAsOnePushWithTheLatest()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<MachineSummary> pushes = listener.Listen<MachineSummary>(LiveEvents.MachineChanged);
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);

        Assert.Equal(MachineState.Pending, (await LiveListener.NextAsync(pushes, m => m.Id == machine.Id)).State);

        (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        await administrator.AssignedAsync(machine.Id, sequence.Id);
        application.Clock.Advance(LiveNotifier.MachinePushInterval);

        // The approval alone never went out.
        MachineSummary trailing = await LiveListener.NextAsync(pushes, m => m.Id == machine.Id);

        Assert.Equal(MachineState.Approved, trailing.State);
        Assert.Equal(sequence.Id, trailing.Deployment?.SequenceId);
    }

    // A machine removed right after a change must not come back to the page with that change's delayed push.
    [Fact]
    public async Task ARemovedMachineTakesItsDelayedPushWithIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<MachineSummary> pushes = listener.Listen<MachineSummary>(LiveEvents.MachineChanged);
        ChannelReader<MachinesRemovedEvent> removals = listener.Listen<MachinesRemovedEvent>(LiveEvents.MachinesRemoved);
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);

        await LiveListener.NextAsync(pushes, m => m.Id == machine.Id);
        (await administrator.PostAsync($"/api/machines/{machine.Id}/reject")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"/api/machines/{machine.Id}")).StatusCode);

        // The event names the machine, so a page drops it without reading the list again.
        MachinesRemovedEvent removal = await LiveListener.NextAsync(removals);
        Assert.Equal([machine.Id], removal.MachineIds);

        application.Clock.Advance(LiveNotifier.MachinePushInterval);

        Assert.False(await PushedWithinAsync(pushes, machine.Id, TimeSpan.FromSeconds(1)));
    }

    // The run history drops the runs of a removed machine, which the run's delayed push would bring back.
    [Fact]
    public async Task ARemovedMachineTakesTheDelayedPushOfItsRunWithIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<RunHistoryItem> runs = listener.Listen<RunHistoryItem>(LiveEvents.RunChanged);
        ChannelReader<MachinesRemovedEvent> removals = listener.Listen<MachinesRemovedEvent>(LiveEvents.MachinesRemoved);
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);

        (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        DeploymentSummary run = await administrator.AssignedAsync(machine.Id, sequence.Id);
        Assert.Equal(DeploymentState.Assigned, (await LiveListener.NextAsync(runs, r => r.Run.Id == run.Id)).Run.State);

        // The rejection cancels the run within the interval, so its push waits, and the removal comes before it.
        (await administrator.PostAsync($"/api/machines/{machine.Id}/reject")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"/api/machines/{machine.Id}")).StatusCode);
        Assert.Equal([machine.Id], (await LiveListener.NextAsync(removals)).MachineIds);

        application.Clock.Advance(LiveNotifier.MachinePushInterval);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        List<RunHistoryItem> late = [];

        try
        {
            await foreach (RunHistoryItem pushed in runs.ReadAllAsync(timeout.Token))
            {
                late.Add(pushed);
            }
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
        }

        Assert.DoesNotContain(late, r => r.Run.Id == run.Id);
    }
}
