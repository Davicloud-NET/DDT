// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Images;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// What a machine is told about the sequences and the run of the other boot environment, on a server that can start
// both: which it lists, when a poll tells it to restart, and that a pick there waits for the restart.
public sealed class RunEnvironmentSwitchTests(BothEnvironmentsApplication application) : IClassFixture<BothEnvironmentsApplication>
{
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

    // A machine approved on the web, whose agent registered as change makes it.
    private async Task<DeployingMachine> ApprovedAsync(Func<AgentRegistration, AgentRegistration> change)
    {
        DeployingMachine machine = await DeployingMachine.RegisterAsync(application, change);
        (await (await application.AdministratorAsync()).PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        await machine.NextAsync();

        return machine;
    }

    private static Func<AgentRegistration, AgentRegistration> WindowsPE(int? sequenceVersion = null) =>
        registration => registration with { Disks = [DeployingMachine.Disk(0)], SequenceVersion = sequenceVersion ?? registration.SequenceVersion };

    // An agent of version 3 never ran in the Linux boot environment, but only the version says what an agent can do.
    private static Func<AgentRegistration, AgentRegistration> LinuxBoot(int? sequenceVersion = null) =>
        registration => DeployingMachine.LinuxBoot()(registration) with { SequenceVersion = sequenceVersion ?? registration.SequenceVersion };

    private static async Task<Dictionary<Guid, AgentEnvironment?>> ListedAsync(DeployingMachine machine, params Guid[] among)
    {
        IReadOnlyList<AgentSequenceChoice> choices = await RegisteredMachine.ReadAsync<IReadOnlyList<AgentSequenceChoice>>(
            await machine.Agent.SequencesAsync(machine.Id, machine.Token));

        return choices.Where(choice => among.Contains(choice.Id)).ToDictionary(choice => choice.Id, choice => choice.Environment);
    }

    [Fact]
    public async Task AMachineListsTheSequencesOfBothEnvironmentsEachWithItsOwn()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine windowsPE = await DeployingMachine.SignedInAsync(application, operatorName, WindowsPE());
        using DeployingMachine linuxBoot = await DeployingMachine.SignedInAsync(application, operatorName, LinuxBoot());
        SequenceView linux = await LinuxAsync();
        SequenceView windows = await WindowsAsync();
        Dictionary<Guid, AgentEnvironment?> both = new() { [linux.Id] = AgentEnvironment.LinuxBoot, [windows.Id] = AgentEnvironment.WindowsPE };

        Assert.Equal(both, await ListedAsync(windowsPE, linux.Id, windows.Id));
        Assert.Equal(both, await ListedAsync(linuxBoot, linux.Id, windows.Id));
    }

    [Fact]
    public async Task WithoutTheOtherEnvironmentOnTheServerAMachineListsOnlyItsOwn()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine windowsPE = await DeployingMachine.SignedInAsync(application, operatorName, WindowsPE());
        using DeployingMachine linuxBoot = await DeployingMachine.SignedInAsync(application, operatorName, LinuxBoot());
        SequenceView linux = await LinuxAsync();
        SequenceView windows = await WindowsAsync();
        application.SetLinuxBoot(false);
        application.SetBootImage(false);

        try
        {
            Assert.Equal(new[] { windows.Id }, (await ListedAsync(windowsPE, linux.Id, windows.Id)).Keys);
            Assert.Equal(new[] { linux.Id }, (await ListedAsync(linuxBoot, linux.Id, windows.Id)).Keys);
        }
        finally
        {
            application.SetLinuxBoot(true);
            application.SetBootImage(true);
        }
    }

    // It could not take its resume token along, so it would lose its sign-in on the way.
    [Fact]
    public async Task AnAgentThatCannotChangeTheEnvironmentListsOnlyItsOwn()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine old = await DeployingMachine.SignedInAsync(application, operatorName, LinuxBoot(sequenceVersion: 3));
        SequenceView windows = await WindowsAsync();

        Assert.Empty(await ListedAsync(old, windows.Id));
    }

    [Fact]
    public async Task ARulesSequenceOfTheOtherEnvironmentIsSuggestedWhenTheMachineCanRestartIntoIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName, WindowsPE());
        SequenceView linux = await LinuxAsync();
        await administrator.CreatedRuleAsync(RuleRequests.MacRule(linux.Id, machine.Registration.PrimaryMac));

        Assert.Equal(linux.Id, (await machine.NextAsync()).SuggestedSequenceId);
    }

    [Fact]
    public async Task APollTellsAMachineToRestartIntoTheEnvironmentItsRunStartsIn()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine windowsPE = await ApprovedAsync(WindowsPE());
        using DeployingMachine linuxBoot = await ApprovedAsync(LinuxBoot());
        SequenceView linux = await LinuxAsync();
        SequenceView windows = await WindowsAsync();

        Assert.Null((await windowsPE.NextAsync()).RestartInto);

        DeploymentSummary run = await administrator.AssignedAsync(windowsPE.Id, linux.Id, "LINUX-40");
        await administrator.AssignedAsync(linuxBoot.Id, windows.Id);
        AgentNextResult inWindowsPE = await windowsPE.NextAsync();
        AgentNextResult inLinuxBoot = await linuxBoot.NextAsync();

        Assert.Null(inWindowsPE.Run);
        Assert.Equal(AgentEnvironment.LinuxBoot, inWindowsPE.RestartInto);
        Assert.Null(inLinuxBoot.Run);
        Assert.Equal(AgentEnvironment.WindowsPE, inLinuxBoot.RestartInto);

        // The run is kept with where it starts, for netboot. It stays assigned across the restart, and the resume
        // token the agent takes along keeps the approval.
        Assert.Equal(
            AgentEnvironment.LinuxBoot,
            await application.QueryAsync(database => database.Deployments.Where(d => d.Id == run.Id).Select(d => d.BootEnvironment).SingleAsync(TestContext.Current.CancellationToken)));

        await windowsPE.RestartIntoAsync(AgentEnvironment.LinuxBoot);
        AgentNextResult arrived = await windowsPE.NextAsync();

        Assert.Equal(run.Id, Assert.IsType<AgentRun>(arrived.Run).Id);
        Assert.Null(arrived.RestartInto);
    }

    // A run from before the server kept where runs start says it through its sequence.
    [Fact]
    public async Task ARunWithoutItsBootEnvironmentStillTellsTheMachineToRestart()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await ApprovedAsync(WindowsPE());
        DeploymentSummary run = await administrator.AssignedAsync(machine.Id, (await LinuxAsync()).Id, "LINUX-41");
        await application.QueryAsync(database => database.Deployments
            .Where(d => d.Id == run.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(d => d.BootEnvironment, (AgentEnvironment?)null), TestContext.Current.CancellationToken));

        Assert.Equal(AgentEnvironment.LinuxBoot, (await machine.NextAsync()).RestartInto);
    }

    [Fact]
    public async Task APollNamesNoRestartIntoAnEnvironmentTheServerCannotStart()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await ApprovedAsync(WindowsPE());
        await administrator.AssignedAsync(machine.Id, (await LinuxAsync()).Id, "LINUX-42");
        application.SetLinuxBoot(false);

        try
        {
            AgentNextResult next = await machine.NextAsync();

            Assert.Null(next.Run);
            Assert.Null(next.RestartInto);
        }
        finally
        {
            application.SetLinuxBoot(true);
        }

        Assert.Equal(AgentEnvironment.LinuxBoot, (await machine.NextAsync()).RestartInto);
    }

    [Fact]
    public async Task APollNamesNoRestartToAnAgentThatCannotChangeTheEnvironment()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine old = await ApprovedAsync(LinuxBoot(sequenceVersion: 3));
        await administrator.AssignedAsync(old.Id, (await WindowsAsync()).Id);

        AgentNextResult next = await old.NextAsync();

        Assert.Null(next.Run);
        Assert.Null(next.RestartInto);
    }

    // The run is made after the restart, by the pick there. So nothing but the approval, the sign-in and what was
    // picked crosses it.
    [Fact]
    public async Task APickOfASequenceOfTheOtherEnvironmentSaysThatTheMachineRestartsFirst()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName, WindowsPE());
        SequenceView linux = await LinuxAsync();

        HttpResponseMessage refused = await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(linux.Id, 0, "LINUX-43"));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            $"{linux.Name} runs in the Linux boot environment, and this machine started Windows PE. " +
            "The machine restarts into the Linux boot environment first. Choose the sequence again, and it does.",
            await TestDatabase.TitleAsync(refused));
        Assert.Null((await application.MachineAsync(machine.Id)).ActiveDeploymentId);
    }

    [Fact]
    public async Task TheWebLearnsWhichBootEnvironmentsTheServerCanStart()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        DeploymentOptionsView both = await RegisteredMachine.ReadAsync<DeploymentOptionsView>(await administrator.GetAsync("/api/deployments/options"));
        application.SetLinuxBoot(false);

        try
        {
            DeploymentOptionsView one = await RegisteredMachine.ReadAsync<DeploymentOptionsView>(await administrator.GetAsync("/api/deployments/options"));

            Assert.Equal([AgentEnvironment.WindowsPE, AgentEnvironment.LinuxBoot], both.BootEnvironments);
            Assert.Equal([AgentEnvironment.WindowsPE], one.BootEnvironments);
        }
        finally
        {
            application.SetLinuxBoot(true);
        }
    }
}
