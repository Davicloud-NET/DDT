// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Images;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// An agent that knows sequences up to version 4, on a server that can start both boot environments: it is offered no
// sequence with steps in the installed Linux, and Windows PE is not restarted for a run of one.
public sealed class InstalledLinuxVersionTests(BothEnvironmentsApplication application) : IClassFixture<BothEnvironmentsApplication>
{
    // The version the agents of the Linux boot environment's first release say they run.
    private const int BootOnlyVersion = 4;

    private const string StaysAssigned =
        "It stays assigned until the machine registers with an agent that runs it. A machine started from the network takes the server's agent.";

    private async Task<SequenceView> SequenceAsync(bool installedSteps = true)
    {
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");
        SequenceDefinition definition = installedSteps ? SequenceRequests.LinuxWithInstalledSteps(image.Id) : SequenceRequests.Linux(image.Id);

        return await (await application.AdministratorAsync()).CreatedSequenceAsync(definition);
    }

    // A machine in Windows PE, approved on the web, whose agent says it runs sequences up to the version.
    private async Task<DeployingMachine> ApprovedInWindowsPEAsync(int sequenceVersion)
    {
        DeployingMachine machine = await DeployingMachine.RegisterAsync(application, [DeployingMachine.Disk(0)], sequenceVersion: sequenceVersion);
        (await (await application.AdministratorAsync()).PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        await machine.NextAsync();

        return machine;
    }

    private Task<Deployment> StoredAsync(Guid runId) =>
        application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == runId, TestContext.Current.CancellationToken));

    // The list shows it with its restart, and the restart is taken. A run of it is still never handed to Windows PE.
    [Fact]
    public async Task AnAgentOfVersion4InWindowsPEMayRestartForASequenceItCannotRunAndIsHandedNoRunOfItThere()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        SequenceView installed = await SequenceAsync();
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName, [DeployingMachine.Disk(0)], sequenceVersion: BootOnlyVersion);

        HttpResponseMessage picked = await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(installed.Id, 0, "LINUX-34"));

        Assert.Equal(HttpStatusCode.Conflict, picked.StatusCode);
        Assert.Equal(
            $"{installed.Name} runs in the Linux boot environment, and this machine started Windows PE. The machine restarts into the " +
            "Linux boot environment first. Choose the sequence again, and it does.",
            await TestDatabase.TitleAsync(picked));
        Assert.Null((await application.MachineAsync(machine.Id)).ActiveDeploymentId);

        (await machine.Agent.EnvironmentAsync(machine.Id, machine.Token, new AgentEnvironmentRequest(AgentEnvironment.LinuxBoot, installed.Id))).EnsureSuccessStatusCode();

        Assert.Equal(AgentEnvironment.LinuxBoot, (await application.MachineAsync(machine.Id)).NextEnvironment);
        Assert.Equal(installed.Id, (await application.MachineAsync(machine.Id)).PendingSequenceId);
        Assert.Null((await application.MachineAsync(machine.Id)).ActiveDeploymentId);
    }

    // Where the machine is, its agent's version decides. For the other boot environment the restart is the cure: it
    // is a start from the network, which brings the server's agent.
    [Fact]
    public async Task AnAgentOfVersion4IsOfferedASequenceWithStepsInTheInstalledLinuxOnlyWhereTheMachineRestartsForIt()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        SequenceView installed = await SequenceAsync();
        SequenceView bootOnly = await SequenceAsync(installedSteps: false);
        using DeployingMachine old = await DeployingMachine.SignedInAsync(
            application,
            operatorName,
            registration => DeployingMachine.LinuxBoot()(registration) with { SequenceVersion = BootOnlyVersion });
        using DeployingMachine current = await DeployingMachine.SignedInAsync(application, operatorName, DeployingMachine.LinuxBoot());
        using DeployingMachine oldWindowsPE = await DeployingMachine.SignedInAsync(
            application,
            operatorName,
            [DeployingMachine.Disk(0)],
            sequenceVersion: BootOnlyVersion);
        using DeployingMachine windowsPE = await DeployingMachine.SignedInAsync(application, operatorName, [DeployingMachine.Disk(0)]);
        Guid[] both = [installed.Id, bootOnly.Id];

        async Task<IReadOnlyList<AgentSequenceChoice>> ChoicesAsync(DeployingMachine machine) =>
            [.. (await RegisteredMachine.ReadAsync<IReadOnlyList<AgentSequenceChoice>>(await machine.Agent.SequencesAsync(machine.Id, machine.Token)))
                .Where(choice => both.Contains(choice.Id))];

        async Task<IEnumerable<Guid>> ListedAsync(DeployingMachine machine) => (await ChoicesAsync(machine)).Select(choice => choice.Id);

        Assert.Equal([bootOnly.Id], await ListedAsync(old));
        Assert.Equal(both.Order(), (await ListedAsync(current)).Order());

        // Windows PE lists what it can restart for, whatever its own agent runs: none of it runs there.
        IReadOnlyList<AgentSequenceChoice> restartsFirst = await ChoicesAsync(oldWindowsPE);
        Assert.Equal(both.Order(), restartsFirst.Select(choice => choice.Id).Order());
        Assert.All(restartsFirst, choice => Assert.Equal(AgentEnvironment.LinuxBoot, choice.Environment));
        Assert.Equal(both.Order(), (await ListedAsync(windowsPE)).Order());

        // A pick of what the list did not show is refused, and nothing is assigned.
        HttpResponseMessage picked = await old.Agent.PickRunAsync(old.Id, old.Token, new AgentRunRequest(installed.Id, 0, "LINUX-32"));

        Assert.Equal(HttpStatusCode.Conflict, picked.StatusCode);
        Assert.Equal(
            $"{installed.Name} needs a newer agent than this machine runs. Start the machine from the network again, so it gets the server's " +
            "agent, and choose it then.",
            await TestDatabase.TitleAsync(picked));
        Assert.Null((await application.MachineAsync(old.Id)).ActiveDeploymentId);

        AgentRun run = await RegisteredMachine.ReadAsync<AgentRun>(
            await current.Agent.PickRunAsync(current.Id, current.Token, new AgentRunRequest(installed.Id, 0, "LINUX-33")));
        Assert.Equal(5, run.Sequence.Version);
    }

    // The restart would cost the machine its Windows PE, and the agent it arrives at could not run the sequence either.
    [Fact]
    public async Task AnAgentOfVersion4InWindowsPEIsNotRestartedForARunWithStepsInTheInstalledLinuxAndIsToldWhy()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await ApprovedInWindowsPEAsync(BootOnlyVersion);
        DeploymentSummary assigned = await administrator.AssignedAsync(machine.Id, (await SequenceAsync()).Id, "LINUX-36");
        string subject = machine.Id.ToString("D");

        foreach (int _ in Enumerable.Range(0, 2))
        {
            AgentNextResult next = await machine.NextAsync();

            Assert.Null(next.Run);
            Assert.Null(next.RestartInto);
            Assert.Equal(
                $"{assigned.Title} runs in the Linux boot environment and does not start in Windows PE. It also needs a newer agent than " +
                $"this machine runs, so the machine is not restarted for it. {StaysAssigned}",
                next.WaitReason);
        }

        Deployment stored = await StoredAsync(assigned.Id);
        Assert.Equal((DeploymentState.Assigned, 0), (stored.State, stored.RestartsOrdered));
        Assert.Null((await application.MachineAsync(machine.Id)).NextEnvironment);
        Assert.False(await application.QueryAsync(database => database.AuditEvents.AnyAsync(
            e => e.SubjectId == subject && e.Action == "machine.environment-requested",
            TestContext.Current.CancellationToken)));

        // Windows PE with an agent that says 5 is restarted for the same run.
        AgentRegistrationResult newer = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { ResumeToken = machine.ResumeToken, SequenceVersion = 5 }));
        AgentNextResult restarted = await RegisteredMachine.ReadAsync<AgentNextResult>(await machine.Agent.NextAsync(machine.Id, newer.Token!));

        Assert.Equal(AgentEnvironment.LinuxBoot, restarted.RestartInto);
        Assert.Null(restarted.WaitReason);
        Assert.Equal(1, (await StoredAsync(assigned.Id)).RestartsOrdered);
    }

    // What the first release of the Linux boot environment could run still gets its restart.
    [Fact]
    public async Task AnAgentOfVersion4InWindowsPEIsRestartedForARunThatEndsInTheLinuxBootEnvironment()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await ApprovedInWindowsPEAsync(BootOnlyVersion);
        DeploymentSummary assigned = await administrator.AssignedAsync(machine.Id, (await SequenceAsync(installedSteps: false)).Id, "LINUX-37");

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(AgentEnvironment.LinuxBoot, next.RestartInto);
        Assert.Null(next.WaitReason);
        Assert.Equal(1, (await StoredAsync(assigned.Id)).RestartsOrdered);
    }

    // With both, a dialog on the web says before the assignment what the poll says after it.
    [Fact]
    public async Task TellsTheWebTheVersionASequenceNeedsAndTheOneAMachinesAgentRuns()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView installed = await SequenceAsync();
        SequenceView bootOnly = await SequenceAsync(installedSteps: false);
        using DeployingMachine old = await ApprovedInWindowsPEAsync(BootOnlyVersion);
        using DeployingMachine current = await ApprovedInWindowsPEAsync(SequenceDefinition.CurrentVersion);

        IReadOnlyList<SequenceSummary> sequences =
            await RegisteredMachine.ReadAsync<IReadOnlyList<SequenceSummary>>(await administrator.GetAsync(SequenceRequests.Sequences));
        IReadOnlyList<MachineSummary> machines =
            await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await administrator.GetAsync("/api/machines"));

        Assert.Equal(5, Assert.Single(sequences, sequence => sequence.Id == installed.Id).Version);
        Assert.Equal(4, Assert.Single(sequences, sequence => sequence.Id == bootOnly.Id).Version);
        Assert.Equal(BootOnlyVersion, Assert.Single(machines, machine => machine.Id == old.Id).SequenceVersion);
        Assert.Equal(SequenceDefinition.CurrentVersion, Assert.Single(machines, machine => machine.Id == current.Id).SequenceVersion);
    }
}
