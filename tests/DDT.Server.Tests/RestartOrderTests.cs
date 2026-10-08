// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// The restarts the server orders for a run of the other boot environment: recorded, counted, and stopped after two.
public sealed class RestartOrderTests(BothEnvironmentsApplication application) : IClassFixture<BothEnvironmentsApplication>
{
    private const string Requested = "machine.environment-requested";

    private const string GaveUp =
        "The machine was restarted for it twice and did not start the Linux boot environment, so it is not restarted again. " +
        "Check that it starts from the network and that no DHCP server names another boot file. " +
        "The run stays assigned and starts once the machine starts the Linux boot environment.";

    private async Task<SequenceView> LinuxAsync()
    {
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");

        return await (await application.AdministratorAsync()).CreatedSequenceAsync(SequenceRequests.Linux(image.Id));
    }

    private async Task<SequenceView> WindowsAsync()
    {
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        return await (await application.AdministratorAsync()).CreatedSequenceAsync(SequenceRequests.Minimal(image.Id));
    }

    private static AgentRegistration WindowsPE(AgentRegistration registration) => registration with { Disks = [DeployingMachine.Disk(0)] };

    // A machine in Windows PE, approved on the web, with a Linux sequence assigned to it there.
    private async Task<(DeployingMachine Machine, DeploymentSummary Run)> WithLinuxRunAsync(Func<AgentRegistration, AgentRegistration>? change = null)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        DeployingMachine machine = await DeployingMachine.RegisterAsync(application, change ?? WindowsPE);
        (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        await machine.NextAsync();

        return (machine, await administrator.AssignedAsync(machine.Id, (await LinuxAsync()).Id, $"LINUX-{Guid.NewGuid():N}"[..15]));
    }

    private Task<Deployment> StoredAsync(Guid runId) =>
        application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == runId, TestContext.Current.CancellationToken));

    private Task<List<string>> AuditAsync(Guid machineId)
    {
        string subject = machineId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .Where(e => e.SubjectId == subject && e.Action == Requested)
            .OrderBy(e => e.Id)
            .Select(e => e.Detail ?? string.Empty)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private async Task<MachineSummary> SummaryAsync(Guid machineId) =>
        (await RegisteredMachine.ReadAsync<List<MachineSummary>>(await (await application.AdministratorAsync()).GetAsync("/api/machines"))).Single(m => m.Id == machineId);

    private static string Where(DeploymentSummary run) => $"{run.Title} runs in the Linux boot environment and does not start in Windows PE.";

    [Fact]
    public async Task ARestartThePollOrdersIsRecordedOnTheMachineCountedOnTheRunAndAudited()
    {
        (DeployingMachine machine, DeploymentSummary run) = await WithLinuxRunAsync();
        using DeployingMachine owned = machine;

        AgentNextResult next = await machine.NextAsync();

        Machine stored = await application.MachineAsync(machine.Id);
        Assert.Equal(AgentEnvironment.LinuxBoot, next.RestartInto);
        Assert.Null(next.WaitReason);
        Assert.Null(next.Run);
        Assert.Equal(AgentEnvironment.LinuxBoot, stored.NextEnvironment);
        Assert.Equal(application.Clock.GetUtcNow(), stored.NextEnvironmentUtc);
        Assert.Null(stored.PendingSequenceId);
        Assert.Equal(1, (await StoredAsync(run.Id)).RestartsOrdered);
        Assert.Equal(
            [$"Restarts from Windows PE into the Linux boot environment for {run.Title}, which is assigned to it. Restart 1 of 2 the server orders for this run."],
            await AuditAsync(machine.Id));

        MachineSummary summary = await SummaryAsync(machine.Id);
        Assert.Equal(AgentEnvironment.LinuxBoot, summary.NextEnvironment);
        Assert.True(summary.ChangesBootEnvironment);
        Assert.Null(summary.RestartsFailed);
    }

    // The answer may not have reached the agent. One that asks again at once is told again, and nothing is counted.
    [Fact]
    public async Task AnAgentThatAsksAgainAtOnceIsToldAgainAndTheRestartCountsOnce()
    {
        (DeployingMachine machine, DeploymentSummary run) = await WithLinuxRunAsync();
        using DeployingMachine owned = machine;
        await machine.NextAsync();

        application.Clock.Advance(DeploymentLimits.RestartOrderOpen);
        AgentNextResult again = await machine.NextAsync();

        Assert.Equal(AgentEnvironment.LinuxBoot, again.RestartInto);
        Assert.Equal(1, (await StoredAsync(run.Id)).RestartsOrdered);
        Assert.Single(await AuditAsync(machine.Id));
    }

    [Fact]
    public async Task AMachineThatArrivesWhereItsRunStartsGetsTheRunAndNothingStaysOfTheRestart()
    {
        (DeployingMachine machine, DeploymentSummary run) = await WithLinuxRunAsync();
        using DeployingMachine owned = machine;
        await machine.NextAsync();

        AgentRegistrationResult registered = await machine.RestartIntoAsync(AgentEnvironment.LinuxBoot);
        AgentNextResult arrived = await machine.NextAsync();

        Machine stored = await application.MachineAsync(machine.Id);
        Assert.Equal(MachineState.Approved, registered.State);
        Assert.Equal(run.Id, Assert.IsType<AgentRun>(arrived.Run).Id);
        Assert.Null(arrived.RestartInto);
        Assert.Null(arrived.WaitReason);
        Assert.Null(stored.NextEnvironment);
        Assert.Null(stored.NextEnvironmentUtc);
        Assert.Equal(0, (await StoredAsync(run.Id)).RestartsOrdered);
    }

    // Netboot named the machine another boot file, or the firmware started from the disk: it is back where it was.
    [Fact]
    public async Task AfterTwoRestartsThatBroughtTheMachineBackTheServerStopsKeepsTheRunAndSaysWhy()
    {
        (DeployingMachine machine, DeploymentSummary run) = await WithLinuxRunAsync();
        using DeployingMachine owned = machine;

        Assert.Equal(AgentEnvironment.LinuxBoot, (await machine.NextAsync()).RestartInto);
        await machine.RestartIntoAsync(AgentEnvironment.WindowsPE);
        Assert.Null((await application.MachineAsync(machine.Id)).NextEnvironment);
        Assert.Null((await SummaryAsync(machine.Id)).RestartsFailed);

        Assert.Equal(AgentEnvironment.LinuxBoot, (await machine.NextAsync()).RestartInto);
        await machine.RestartIntoAsync(AgentEnvironment.WindowsPE);
        Assert.Equal(AgentEnvironment.LinuxBoot, (await SummaryAsync(machine.Id)).RestartsFailed);

        foreach (int _ in Enumerable.Range(0, 3))
        {
            AgentNextResult stopped = await machine.NextAsync();

            Assert.Null(stopped.RestartInto);
            Assert.Null(stopped.Run);
            Assert.Equal($"{Where(run)} {GaveUp}", stopped.WaitReason);
            application.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        Deployment kept = await StoredAsync(run.Id);
        MachineSummary summary = await SummaryAsync(machine.Id);
        Assert.Equal(DeploymentState.Assigned, kept.State);
        Assert.Equal(2, kept.RestartsOrdered);
        Assert.Equal(run.Id, (await application.MachineAsync(machine.Id)).ActiveDeploymentId);
        Assert.Equal(AgentEnvironment.LinuxBoot, summary.RestartsFailed);
        Assert.Null(summary.NextEnvironment);
        Assert.Equal(DeploymentState.Assigned, summary.Deployment!.State);
        Assert.Equal(
            ["Restart 1 of 2 the server orders for this run.", "Restart 2 of 2 the server orders for this run."],
            (await AuditAsync(machine.Id)).Select(detail => detail[detail.IndexOf("Restart ", StringComparison.Ordinal)..]));
    }

    // A BIOS, or firmware that keeps no variable: the agent restarts each time, loses its token on the way and registers
    // anew. In a zero touch network nobody has to sign in for that, so nothing else would ever stop it.
    [Fact]
    public async Task AMachineThatRegistersAnewAfterEveryRestartInAZeroTouchNetworkIsNotRestartedWithoutEnd()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application, WindowsPE);
        using AgentClient lab = new(application.CreateDefaultClient(), "10.200.3.4");
        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = application.Clock.GetUtcNow() - TimeSpan.FromMinutes(10));
        MachineSummary assigned = await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.AssignAsync(machine.Id, (await LinuxAsync()).Id, "LINUX-ZT"));
        DeploymentSummary run = assigned.Deployment!;
        List<AgentEnvironment?> ordered = [];
        AgentNextResult next = await NetbootAsync();

        async Task<AgentNextResult> NetbootAsync()
        {
            Assert.Equal(MachineState.Approved, (await machine.RegisterAgainAsync(lab)).State);
            application.Clock.Advance(TimeSpan.FromSeconds(40));

            return await machine.NextAsync();
        }

        for (int start = 0; start < 8; start++)
        {
            ordered.Add(next.RestartInto);
            Assert.Null(next.Run);
            next = await NetbootAsync();
        }

        Assert.Equal(MachineState.Pending, assigned.State);
        Assert.Equal([AgentEnvironment.LinuxBoot, AgentEnvironment.LinuxBoot, null, null, null, null, null, null], ordered);
        Assert.Equal($"{Where(run)} {GaveUp}", next.WaitReason);
        Assert.Equal(DeploymentState.Assigned, (await StoredAsync(run.Id)).State);
        Assert.Equal(2, (await AuditAsync(machine.Id)).Count);

        // Once the DHCP server is put right, the machine's next start from the network is the Linux boot environment.
        Assert.Equal(MachineState.Approved, (await machine.RegisterAgainAsync(lab, AgentEnvironment.LinuxBoot)).State);
        Assert.Equal(0, (await StoredAsync(run.Id)).RestartsOrdered);
        Assert.Null((await SummaryAsync(machine.Id)).RestartsFailed);

        AgentNextResult arrived = await machine.NextAsync();

        Assert.Equal(run.Id, Assert.IsType<AgentRun>(arrived.Run).Id);
        Assert.Null(arrived.WaitReason);
    }

    // The agent's own guard refuses a second restart in one start, or the agent cannot restart the machine at all. It
    // goes on polling, which an agent that restarts never does.
    [Fact]
    public async Task AnAgentThatGoesOnPollingInsteadOfRestartingIsAskedTwiceAndThenToldWhy()
    {
        (DeployingMachine machine, DeploymentSummary run) = await WithLinuxRunAsync();
        using DeployingMachine owned = machine;

        Assert.Equal(AgentEnvironment.LinuxBoot, (await machine.NextAsync()).RestartInto);
        application.Clock.Advance(DeploymentLimits.RestartOrderOpen + TimeSpan.FromSeconds(1));
        Assert.Equal(AgentEnvironment.LinuxBoot, (await machine.NextAsync()).RestartInto);
        Assert.Equal(2, (await StoredAsync(run.Id)).RestartsOrdered);

        application.Clock.Advance(DeploymentLimits.RestartOrderOpen);
        Assert.Equal(AgentEnvironment.LinuxBoot, (await machine.NextAsync()).RestartInto);

        application.Clock.Advance(TimeSpan.FromSeconds(1));
        AgentNextResult stopped = await machine.NextAsync();

        Assert.Null(stopped.RestartInto);
        Assert.Equal($"{Where(run)} {GaveUp}", stopped.WaitReason);
        Assert.Null((await application.MachineAsync(machine.Id)).NextEnvironment);
        Assert.Equal(AgentEnvironment.LinuxBoot, (await SummaryAsync(machine.Id)).RestartsFailed);
        Assert.Equal(2, (await AuditAsync(machine.Id)).Count);
    }

    [Fact]
    public async Task AnOperatorWhoAssignsAgainGetsTheMachineRestartedAgain()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (DeployingMachine machine, DeploymentSummary run) = await WithLinuxRunAsync();
        using DeployingMachine owned = machine;

        foreach (int _ in Enumerable.Range(0, 2))
        {
            Assert.Equal(AgentEnvironment.LinuxBoot, (await machine.NextAsync()).RestartInto);
            await machine.RestartIntoAsync(AgentEnvironment.WindowsPE);
        }

        Assert.Null((await machine.NextAsync()).RestartInto);

        // The run that ends takes its count with it, and the next one starts at none.
        (await administrator.EndCurrentAsync(machine.Id)).EnsureSuccessStatusCode();
        AgentNextResult without = await machine.NextAsync();
        Assert.Null(without.RestartInto);
        Assert.Null(without.WaitReason);
        Assert.Null((await SummaryAsync(machine.Id)).RestartsFailed);

        DeploymentSummary again = await administrator.AssignedAsync(machine.Id, (await LinuxAsync()).Id, "LINUX-AGAIN");
        AgentNextResult next = await machine.NextAsync();

        Assert.NotEqual(run.Id, again.Id);
        Assert.Equal(AgentEnvironment.LinuxBoot, next.RestartInto);
        Assert.Equal(1, (await StoredAsync(again.Id)).RestartsOrdered);
        Assert.Equal(2, (await StoredAsync(run.Id)).RestartsOrdered);
    }

    // Netboot names such a machine the Windows PE boot file, so a restart for the Linux boot environment ends in
    // Windows PE again. The shim here is a real one, signed under Microsoft's 2011 CA.
    [Fact]
    public async Task AMachineWhoseFirmwareRefusesTheShimIsNotRestartedForItWhereWindowsPEStartsInstead()
    {
        TestLinuxBoot.Write(application.LinuxBootFolder, TestPe.Fixture("shimx64.efi.dualsigned"));

        try
        {
            SignedInClient administrator = await application.AdministratorAsync();
            using DeployingMachine machine = await DeployingMachine.RegisterAsync(application, registration => WindowsPE(registration) with
            {
                SecureBootEnabled = true,
                TrustedUefiCas = UefiCa.Microsoft2023,
            });
            (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
            await machine.NextAsync();
            SequenceView linux = await LinuxAsync();
            MachineSummary assigned = await RegisteredMachine.ReadAsync<MachineSummary>(await administrator.AssignAsync(
                machine.Id,
                new AssignSequenceRequest(linux.Id, "LINUX-SB", AllowSecureBootMismatch: true)));

            AgentNextResult refused = await machine.NextAsync();

            Assert.Null(refused.RestartInto);
            Assert.Equal(
                $"{Where(assigned.Deployment!)} This machine is not started in the Linux boot environment: Secure Boot is on, and its firmware does not " +
                "trust the certificate authority that signs the environment's first loader. Allow it in the firmware setup, or turn Secure Boot off there.",
                refused.WaitReason);
            Assert.Null((await application.MachineAsync(machine.Id)).NextEnvironment);
            Assert.Empty(await AuditAsync(machine.Id));

            // Without a boot image netboot names the shim after all, so the restart is worth a try.
            application.SetBootImage(false);

            try
            {
                Assert.Equal(AgentEnvironment.LinuxBoot, (await machine.NextAsync()).RestartInto);
            }
            finally
            {
                application.SetBootImage(true);
            }
        }
        finally
        {
            TestLinuxBoot.Write(application.LinuxBootFolder);
        }
    }

    [Fact]
    public async Task AnAgentTooOldToChangeTheEnvironmentIsToldWhyItsRunWaits()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine old = await DeployingMachine.RegisterAsync(application, registration => DeployingMachine.LinuxBoot()(registration) with { SequenceVersion = 3 });
        (await administrator.PostAsync($"/api/machines/{old.Id}/approve")).EnsureSuccessStatusCode();
        await old.NextAsync();
        DeploymentSummary run = await administrator.AssignedAsync(old.Id, (await WindowsAsync()).Id);

        AgentNextResult next = await old.NextAsync();

        Assert.Null(next.RestartInto);
        Assert.Equal(
            $"{run.Title} runs in Windows PE and does not start in the Linux boot environment. This machine's agent is too old to change the boot " +
            "environment. Start the machine from the network again, so it gets the server's agent.",
            next.WaitReason);
        Assert.False((await SummaryAsync(old.Id)).ChangesBootEnvironment);
        Assert.Equal(0, (await StoredAsync(run.Id)).RestartsOrdered);
    }

    [Fact]
    public async Task WithoutTheEnvironmentOnTheServerTheAgentIsToldThatTheRunWaitsForIt()
    {
        (DeployingMachine machine, DeploymentSummary run) = await WithLinuxRunAsync();
        using DeployingMachine owned = machine;
        application.SetLinuxBoot(false);

        try
        {
            AgentNextResult next = await machine.NextAsync();

            Assert.Null(next.RestartInto);
            Assert.Equal(
                $"{Where(run)} This server has no Linux boot environment to start. A release of DDT brings it in the folder linux-boot next to the server.",
                next.WaitReason);
        }
        finally
        {
            application.SetLinuxBoot(true);
        }

        Assert.Equal(AgentEnvironment.LinuxBoot, (await machine.NextAsync()).RestartInto);
    }

    // A machine that only waits, or whose run starts where it is, is told nothing.
    [Fact]
    public async Task AMachineWhoseRunStartsHereOrThatHasNoneIsToldNoReason()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application, WindowsPE);
        Assert.Null((await machine.NextAsync()).WaitReason);
        (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        Assert.Null((await machine.NextAsync()).WaitReason);

        DeploymentSummary run = await administrator.AssignedAsync(machine.Id, (await WindowsAsync()).Id);
        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(run.Id, Assert.IsType<AgentRun>(next.Run).Id);
        Assert.Null(next.RestartInto);
        Assert.Null(next.WaitReason);
    }
}
