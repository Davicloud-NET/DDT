// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// What a machine keeps across a restart into the other boot environment with its resume token, and for how long.
public sealed class EnvironmentResumeTests(BothEnvironmentsApplication application) : IClassFixture<BothEnvironmentsApplication>
{
    private static readonly TimeSpan s_second = TimeSpan.FromSeconds(1);

    // A machine in Windows PE that an operator signed in at, which said that it restarts for a Linux sequence.
    private async Task<(DeployingMachine Machine, SequenceView Linux, string OperatorName)> AskedToRestartAsync()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName, [DeployingMachine.Disk(0)]);
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");
        SequenceView linux = await (await application.AdministratorAsync()).CreatedSequenceAsync(SequenceRequests.Linux(image.Id));

        (await machine.Agent.EnvironmentAsync(machine.Id, machine.Token, new AgentEnvironmentRequest(AgentEnvironment.LinuxBoot, linux.Id))).EnsureSuccessStatusCode();

        return (machine, linux, operatorName);
    }

    private static async Task<IReadOnlyList<AgentSequenceChoice>> ChoicesAsync(DeployingMachine machine) =>
        await RegisteredMachine.ReadAsync<IReadOnlyList<AgentSequenceChoice>>(await machine.Agent.SequencesAsync(machine.Id, machine.Token));

    private async Task<List<string?>> ReregisteredAsync(Guid machineId)
    {
        string subject = machineId.ToString("D");

        return await application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject && e.Action == AuditActions.MachineReregistered)
            .OrderBy(e => e.Id)
            .Select(e => e.Detail)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheResumeTokenKeepsApprovalAndSignInFifteenMinutesAfterTheMachineSaidItRestarts()
    {
        (DeployingMachine machine, SequenceView linux, string operatorName) = await AskedToRestartAsync();
        using DeployingMachine owned = machine;
        int generation = (await application.MachineAsync(machine.Id)).TokenGeneration;
        application.Clock.Advance(DeploymentLimits.EnvironmentSwitch);

        AgentRegistrationResult registered = await machine.RestartIntoAsync(AgentEnvironment.LinuxBoot);

        Assert.Equal((machine.Id, MachineState.Approved, operatorName), (registered.MachineId, registered.State, registered.SignedInBy));

        // The machine is where it was asked to go, so the restart is over. What was picked for it waits to be picked.
        Machine stored = await application.MachineAsync(machine.Id);
        Assert.Equal((AgentEnvironment.LinuxBoot, generation), (stored.AgentEnvironment, stored.TokenGeneration));
        Assert.Null(stored.NextEnvironment);
        Assert.Null(stored.NextEnvironmentUtc);
        Assert.Equal(linux.Id, stored.PendingSequenceId);
        Assert.Empty(await ReregisteredAsync(machine.Id));
    }

    [Fact]
    public async Task ASecondLaterTheMachineStartsOverAndSomeoneHasToSignInAgain()
    {
        (DeployingMachine machine, _, _) = await AskedToRestartAsync();
        using DeployingMachine owned = machine;
        int generation = (await application.MachineAsync(machine.Id)).TokenGeneration;
        application.Clock.Advance(DeploymentLimits.EnvironmentSwitch + s_second);

        AgentRegistrationResult registered = await machine.RestartIntoAsync(AgentEnvironment.LinuxBoot);

        Assert.Equal((machine.Id, MachineState.Pending), (registered.MachineId, registered.State));
        Assert.Null(registered.SignedInBy);

        Machine stored = await application.MachineAsync(machine.Id);
        Assert.Equal(generation + 1, stored.TokenGeneration);
        Assert.Null(stored.SignedInByUserId);
        Assert.Null(stored.NextEnvironment);
        Assert.Null(stored.NextEnvironmentUtc);
        Assert.Null(stored.PendingSequenceId);
        Assert.Equal(["Was Approved."], await ReregisteredAsync(machine.Id));
    }

    // Nothing here knows of a restart, so the token is as good from another environment as it was before machines
    // changed theirs: a run assigned on the web makes the agent restart without telling the server first.
    [Fact]
    public async Task WithoutSuchARequestTheResumeTokenCrossesAsItAlwaysDid()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName, [DeployingMachine.Disk(0)]);
        application.Clock.Advance(TimeSpan.FromHours(1));

        AgentRegistrationResult registered = await machine.RestartIntoAsync(AgentEnvironment.LinuxBoot);

        Assert.Equal((MachineState.Approved, operatorName), (registered.State, registered.SignedInBy));
    }

    // Whoever knows the machine's UUID and MAC can register as it, and gets a machine that waits.
    [Fact]
    public async Task WithoutTheResumeTokenTheMachineStartsOverEvenRightAfterItSaidItRestarts()
    {
        (DeployingMachine machine, _, _) = await AskedToRestartAsync();
        using DeployingMachine owned = machine;

        AgentRegistrationResult registered = await machine.RestartIntoAsync(AgentEnvironment.LinuxBoot, carried: false);

        Assert.Equal((machine.Id, MachineState.Pending), (registered.MachineId, registered.State));
        Assert.Null(registered.SignedInBy);
        Assert.Null((await application.MachineAsync(machine.Id)).SignedInByUserId);
    }

    // The token is from before the machine started over, which bumped its generation. Approved again since, the
    // machine would be worth taking over.
    [Fact]
    public async Task AResumeTokenOfAnOlderGenerationDoesNotCross()
    {
        (DeployingMachine machine, _, _) = await AskedToRestartAsync();
        using DeployingMachine owned = machine;
        string old = machine.ResumeToken;
        await machine.RegisterAgainAsync();
        (await (await application.AdministratorAsync()).PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        int generation = (await application.MachineAsync(machine.Id)).TokenGeneration;

        AgentRegistrationResult registered = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { Environment = AgentEnvironment.LinuxBoot, ResumeToken = old }));

        Assert.Equal(MachineState.Pending, registered.State);
        Assert.Equal(generation + 1, (await application.MachineAsync(machine.Id)).TokenGeneration);
    }

    // The other environment did not start, and the agent is back where it was with the token it took along.
    [Fact]
    public async Task BackInTheSameEnvironmentTheTokenResumesAndARestartThatTookTooLongIsForgotten()
    {
        (DeployingMachine machine, _, string operatorName) = await AskedToRestartAsync();
        using DeployingMachine owned = machine;

        AgentRegistrationResult soon = await machine.RestartIntoAsync(AgentEnvironment.WindowsPE);
        Machine kept = await application.MachineAsync(machine.Id);
        application.Clock.Advance(DeploymentLimits.EnvironmentSwitch + s_second);
        AgentRegistrationResult late = await machine.RestartIntoAsync(AgentEnvironment.WindowsPE);
        Machine forgotten = await application.MachineAsync(machine.Id);

        Assert.Equal((MachineState.Approved, operatorName), (soon.State, soon.SignedInBy));
        Assert.Equal(AgentEnvironment.LinuxBoot, kept.NextEnvironment);
        Assert.NotNull(kept.PendingSequenceId);
        Assert.Equal((MachineState.Approved, operatorName), (late.State, late.SignedInBy));
        Assert.Null(forgotten.NextEnvironment);
        Assert.Null(forgotten.NextEnvironmentUtc);
        Assert.Null(forgotten.PendingSequenceId);
    }

    [Fact]
    public async Task AMachineThatStillPollsLongAfterItSaidItRestartsIsNoLongerExpectedElsewhere()
    {
        (DeployingMachine machine, SequenceView linux, _) = await AskedToRestartAsync();
        using DeployingMachine owned = machine;

        application.Clock.Advance(DeploymentLimits.EnvironmentSwitch);
        await machine.NextAsync();
        Machine atTheEdge = await application.MachineAsync(machine.Id);
        application.Clock.Advance(s_second);
        await machine.NextAsync();
        Machine after = await application.MachineAsync(machine.Id);

        Assert.Equal(AgentEnvironment.LinuxBoot, atTheEdge.NextEnvironment);
        Assert.Equal(linux.Id, atTheEdge.PendingSequenceId);
        Assert.Null(after.NextEnvironment);
        Assert.Null(after.NextEnvironmentUtc);
        Assert.Null(after.PendingSequenceId);
    }

    [Fact]
    public async Task AfterTheRestartTheSequencePickedBeforeItIsOfferedFirst()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (DeployingMachine machine, SequenceView linux, _) = await AskedToRestartAsync();
        using DeployingMachine owned = machine;

        // A rule that chooses another sequence only comes second.
        Image other = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"trixie {Guid.NewGuid():N}");
        SequenceView ruled = await administrator.CreatedSequenceAsync(SequenceRequests.Linux(other.Id));
        await administrator.CreatedRuleAsync(RuleRequests.MacRule(ruled.Id, machine.Registration.PrimaryMac));

        await machine.RestartIntoAsync(AgentEnvironment.LinuxBoot);
        AgentNextResult next = await machine.NextAsync();
        IReadOnlyList<AgentSequenceChoice> choices = await ChoicesAsync(machine);

        Assert.True(next.CanPickSequence);
        Assert.Equal(linux.Id, next.SuggestedSequenceId);
        Assert.True(Assert.Single(choices, c => c.Id == linux.Id).Suggested);
        Assert.Equal(AgentEnvironment.LinuxBoot, Assert.Single(choices, c => c.Id == linux.Id).Environment);
        Assert.False(Assert.Single(choices, c => c.Id == ruled.Id).Suggested);
    }

    // The run is made where it starts, after the restart, from the pick made there. That uses the earlier pick up.
    [Fact]
    public async Task PickingItThereMakesTheRunAndUsesThePickUp()
    {
        (DeployingMachine machine, SequenceView linux, _) = await AskedToRestartAsync();
        using DeployingMachine owned = machine;
        await machine.RestartIntoAsync(AgentEnvironment.LinuxBoot);
        await machine.NextAsync();

        AgentRun run = await RegisteredMachine.ReadAsync<AgentRun>(
            await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(linux.Id, 0, "LINUX-30")));

        Assert.Equal(DeploymentState.Assigned, run.State);
        Assert.Equal(run.Id, Assert.IsType<AgentRun>((await machine.NextAsync()).Run).Id);
        Assert.Null((await application.MachineAsync(machine.Id)).PendingSequenceId);
    }

    // The firmware lost the token, so the machine registers fresh where it was asked to go. Whoever signs in there
    // again is offered the sequence first: it grants nothing.
    [Fact]
    public async Task ThePickSurvivesAFreshStartInTheOtherEnvironmentUntilSomeoneSignsInAgain()
    {
        (DeployingMachine machine, SequenceView linux, string operatorName) = await AskedToRestartAsync();
        using DeployingMachine owned = machine;

        AgentRegistrationResult registered = await machine.RestartIntoAsync(AgentEnvironment.LinuxBoot, carried: false);
        Machine stored = await application.MachineAsync(machine.Id);

        Assert.Equal(MachineState.Pending, registered.State);
        Assert.Null(stored.NextEnvironment);
        Assert.Equal(linux.Id, stored.PendingSequenceId);
        Assert.Null((await machine.NextAsync()).SuggestedSequenceId);

        await machine.SignInAsync(operatorName);

        Assert.Equal(linux.Id, (await machine.NextAsync()).SuggestedSequenceId);
    }

    // A machine that starts over where it was, or anywhere once the restart took too long, forgets the pick.
    [Fact]
    public async Task AFreshStartWhereTheMachineWasForgetsThePick()
    {
        (DeployingMachine machine, _, _) = await AskedToRestartAsync();
        using DeployingMachine owned = machine;

        await machine.RegisterAgainAsync();
        Machine stored = await application.MachineAsync(machine.Id);

        Assert.Equal(MachineState.Pending, stored.State);
        Assert.Null(stored.PendingSequenceId);

        // The restart may still be under way, so netboot keeps starting the other environment until it is over.
        Assert.Equal(AgentEnvironment.LinuxBoot, stored.NextEnvironment);
    }

    // The run says where the machine starts from then on.
    [Fact]
    public async Task ARunAssignedOnTheWebEndsTheRestartAndForgetsThePick()
    {
        (DeployingMachine machine, SequenceView linux, _) = await AskedToRestartAsync();
        using DeployingMachine owned = machine;

        await (await application.AdministratorAsync()).AssignedAsync(machine.Id, linux.Id, "LINUX-31");
        Machine stored = await application.MachineAsync(machine.Id);

        Assert.NotNull(stored.ActiveDeploymentId);
        Assert.Null(stored.NextEnvironment);
        Assert.Null(stored.NextEnvironmentUtc);
        Assert.Null(stored.PendingSequenceId);
    }
}
