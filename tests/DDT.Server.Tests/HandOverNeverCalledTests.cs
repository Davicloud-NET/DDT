// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// What a Linux run's end says when its agent went silent for longer than its token lasts, after the hand-over. The
// server's clock stands still, as in AbandonedRunTests.
public sealed class HandOverNeverCalledTests(ManualClockApplication application) : IClassFixture<ManualClockApplication>
{
    private static readonly TimeSpan s_canNoLongerResume = MachineTokenLifetimes.Run + MachineLogLimits.LastSeenResolution + TimeSpan.FromSeconds(1);

    private async Task<DateTimeOffset> LastSeenLongAgoAsync(Guid machineId)
    {
        DateTimeOffset lastSeen = application.Clock.GetUtcNow() - s_canNoLongerResume;

        await application.ChangeMachineAsync(machineId, m => m.LastSeenUtc = lastSeen);

        return lastSeen;
    }

    private async Task<DeploymentSummary> SweptAsync(Guid runId)
    {
        Assert.Equal(1, await application.Services.GetRequiredService<AbandonedRunSweeper>().SweepOnceAsync(TestContext.Current.CancellationToken));

        return (await (await application.AdministratorAsync()).RunAsync(runId)).Summary;
    }

    // The boot environment started again with the token and sent the machine back. The run's last change is that.
    [Theory]
    [InlineData(1, "once")]
    [InlineData(2, "twice")]
    public async Task AfterAReturnTheEndSaysWhenTheMachineLastCameBackAndNotThatItWasHandedOverThen(int returns, string times)
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, $"LINUX-8{returns}");
        using DeployingMachine owned = machine;

        for (int count = 1; count <= returns; count++)
        {
            application.Clock.Advance(TimeSpan.FromMinutes(3));
            AgentRegistrationResult registered = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
                await machine.Agent.RegisterAsync(machine.Registration with { RunToken = machine.RunToken }));
            (await machine.Agent.RunReportAsync(machine.Id, Assert.IsType<string>(registered.Token), run.Id, HandedOverLinuxRun.Returned(count))).EnsureSuccessStatusCode();
        }

        DateTimeOffset last = (await (await application.AdministratorAsync()).RunAsync(run.Id)).Summary.UpdatedUtc;
        await LastSeenLongAgoAsync(machine.Id);

        DeploymentSummary failed = await SweptAsync(run.Id);

        Assert.Equal(DeploymentState.Failed, failed.State);
        Assert.Equal(
            $"The installed Linux has not called since the run was handed over. The machine started from the network again {times} after " +
            $"that and was sent back to its disk, last at {last:u}, and the run could no longer go on. The machine did not start from " +
            "its disk, or cloud-init did not install DDT's agent there.",
            failed.Error);
    }

    // An installed Linux that called and then went silent is an agent out of contact, as in any other run.
    [Fact]
    public async Task ARunTheInstalledLinuxHadTakenKeepsTheSentenceOfAnAgentOutOfContact()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, "LINUX-83");
        using DeployingMachine owned = machine;
        await HandedOverLinuxRun.InstalledAsync(machine);
        DateTimeOffset lastSeen = await LastSeenLongAgoAsync(machine.Id);

        DeploymentSummary failed = await SweptAsync(run.Id);

        Assert.Equal($"The agent has not been in contact since {lastSeen:u} and could no longer resume the run.", failed.Error);
    }
}
