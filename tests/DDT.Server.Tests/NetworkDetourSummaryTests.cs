// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Threading.Channels;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Server.Deployments;
using DDT.Server.Live;
using Xunit;

namespace DDT.Server.Tests;

// The machine's summary says how often in a row the machine started from the network while its run goes on in the
// installed Linux, and the hub pushes it, so the machine's page says it without reading anything again.
public sealed class NetworkDetourSummaryTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private async Task<MachineSummary> ShownAsync(Guid machineId) => Assert.Single(
        await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await (await application.AdministratorAsync()).GetAsync("/api/machines")),
        shown => shown.Id == machineId);

    [Fact]
    public async Task TheSummaryCarriesTheCountWithEachCountedStartAndLosesItWhenTheInstalledLinuxCalls()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, "LINUX-90");
        using DeployingMachine owned = machine;
        await HandedOverLinuxRun.InstalledAsync(machine);
        Assert.Null((await ShownAsync(machine.Id)).Detours);
        await using LiveListener listener = await LiveListener.StartAsync(application, await application.AdministratorAsync());
        ChannelReader<MachineSummary> pushes = listener.Listen<MachineSummary>(LiveEvents.MachineChanged);
        Guid start = Guid.NewGuid();

        // Each counted start reaches the page by a push.
        await HandedOverLinuxRun.AskAsync(machine, run.Id, start);
        MachineSummary once = await LiveListener.NextAsync(pushes, pushed => pushed.Id == machine.Id && pushed.Detours is not null);
        Assert.Equal((1, DeploymentState.Running, AgentEnvironment.Linux), (once.Detours, once.Deployment?.State, once.AgentEnvironment));
        Assert.Null(once.HandedOverUtc);

        // The same start asking again is not counted again.
        await HandedOverLinuxRun.AskAsync(machine, run.Id, start);
        Assert.Equal(1, (await ShownAsync(machine.Id)).Detours);

        await HandedOverLinuxRun.AskAsync(machine, run.Id, Guid.NewGuid());
        Assert.Equal(2, (await LiveListener.NextAsync(pushes, pushed => pushed.Id == machine.Id && pushed.Detours != 1)).Detours);
        Assert.Equal(2, (await ShownAsync(machine.Id)).Detours);

        // The installed Linux calls, which sets the count back, and the page hears of that too.
        await HandedOverLinuxRun.InstalledAsync(machine);
        MachineSummary called = await LiveListener.NextAsync(pushes, pushed => pushed.Id == machine.Id && pushed.Detours != 2);
        Assert.Null(called.Detours);
        Assert.Equal(DeploymentState.Running, called.Deployment?.State);
        Assert.Null((await ShownAsync(machine.Id)).Detours);
    }

    // The run's error says why it ended, so nothing is counted beside it.
    [Fact]
    public async Task ARunTheFifthStartEndedShowsNoCount()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, "LINUX-91");
        using DeployingMachine owned = machine;
        await HandedOverLinuxRun.InstalledAsync(machine);

        for (int detour = 1; detour < NetworkDetours.Max; detour++)
        {
            await HandedOverLinuxRun.AskAsync(machine, run.Id, Guid.NewGuid());
            Assert.Equal(detour, (await ShownAsync(machine.Id)).Detours);
        }

        await HandedOverLinuxRun.AskAsync(machine, run.Id, Guid.NewGuid());

        MachineSummary ended = await ShownAsync(machine.Id);
        Assert.Equal((DeploymentState.Failed, NetworkDetours.TooMany), (ended.Deployment?.State, ended.Deployment?.Error));
        Assert.Null(ended.Detours);
    }

    // The boot environment has the machine while the run waits for the installed Linux's first call.
    [Fact]
    public async Task AStartCountedBeforeTheInstalledLinuxEverCalledIsShownBesideTheWait()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, "LINUX-92");
        using DeployingMachine owned = machine;

        await HandedOverLinuxRun.AskAsync(machine, run.Id, Guid.NewGuid());

        MachineSummary shown = await ShownAsync(machine.Id);
        Assert.Equal(1, shown.Detours);
        Assert.NotNull(shown.HandedOverUtc);
        Assert.Null(shown.BootReturns);
    }
}
