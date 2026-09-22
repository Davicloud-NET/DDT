// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Threading.Channels;
using DDT.Contracts.Machines;
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

    // A machine removed right after a change must not come back to the page with that change's delayed push.
    [Fact]
    public async Task ARemovedMachineTakesItsDelayedPushWithIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<MachineSummary> pushes = listener.Listen<MachineSummary>(LiveEvents.MachineChanged);
        ChannelReader<DateTimeOffset> removals = listener.Listen(LiveEvents.MachinesRemoved);
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);

        await LiveListener.NextAsync(pushes, m => m.Id == machine.Id);
        (await administrator.PostAsync($"/api/machines/{machine.Id}/reject")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"/api/machines/{machine.Id}")).StatusCode);
        await LiveListener.NextAsync(removals);

        application.Clock.Advance(LiveNotifier.MachinePushInterval);

        Assert.False(await PushedWithinAsync(pushes, machine.Id, TimeSpan.FromSeconds(1)));
    }
}
