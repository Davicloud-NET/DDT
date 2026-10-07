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
using DDT.Server.Deployments;
using DDT.Server.Images;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// Which boot environment is handed which run, which sequences it lists at the machine and which phases it reports.
// Windows PE starts the sequences that install Windows, and the Linux boot environment those that write a raw disk image.
public sealed class RunEnvironmentTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string AskAgain = "Ask the server for the current run.";

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

    // A machine that started the Linux boot environment and was approved on the web.
    private async Task<DeployingMachine> ApprovedInLinuxBootAsync()
    {
        DeployingMachine machine = await DeployingMachine.RegisterAsync(application, DeployingMachine.LinuxBoot());
        (await (await application.AdministratorAsync()).PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        await machine.NextAsync();

        return machine;
    }

    private static async Task<IReadOnlyList<AgentSequenceChoice>> ChoicesAsync(DeployingMachine machine) =>
        await RegisteredMachine.ReadAsync<IReadOnlyList<AgentSequenceChoice>>(await machine.Agent.SequencesAsync(machine.Id, machine.Token));

    [Fact]
    public async Task KeepsTheBootEnvironmentAMachineRegisteredFrom()
    {
        using DeployingMachine linuxBoot = await DeployingMachine.RegisterAsync(application, DeployingMachine.LinuxBoot());
        using DeployingMachine windowsPE = await DeployingMachine.RegisterAsync(application);

        Assert.Equal(AgentEnvironment.LinuxBoot, (await application.MachineAsync(linuxBoot.Id)).AgentEnvironment);
        Assert.Equal(AgentEnvironment.WindowsPE, (await application.MachineAsync(windowsPE.Id)).AgentEnvironment);
    }

    // The run stays assigned while the machine is in the other boot environment. Once it registers from the Linux boot
    // environment with its resume token, which keeps its approval and its run, it gets the run.
    [Fact]
    public async Task AnAssignedLinuxRunWaitsUntilTheMachineStartsTheLinuxBootEnvironment()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator, [DeployingMachine.Disk(0)]);
        SequenceView sequence = await LinuxAsync();

        DeploymentSummary run = await administrator.AssignedAsync(machine.Id, sequence.Id, "LINUX-10");
        AgentNextResult inWindowsPE = await machine.NextAsync();

        Assert.Null(inWindowsPE.Run);
        Assert.Equal(MachineState.Approved, inWindowsPE.State);
        Assert.Equal(DeploymentState.Assigned, (await administrator.RunAsync(run.Id)).Summary.State);

        AgentRegistrationResult registered = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { ResumeToken = machine.ResumeToken, Environment = AgentEnvironment.LinuxBoot }));
        AgentNextResult inLinuxBoot = await RegisteredMachine.ReadAsync<AgentNextResult>(await machine.Agent.NextAsync(machine.Id, registered.Token!));

        Assert.Equal((machine.Id, MachineState.Approved), (registered.MachineId, registered.State));
        Assert.Equal(run.Id, Assert.IsType<AgentRun>(inLinuxBoot.Run).Id);
        Assert.Equal(AgentEnvironment.LinuxBoot, (await application.MachineAsync(machine.Id)).AgentEnvironment);
    }

    [Fact]
    public async Task TheLinuxBootEnvironmentIsHandedNoRunThatInstallsWindows()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine linuxBoot = await ApprovedInLinuxBootAsync();
        using DeployingMachine windowsPE = await DeployingMachine.ApprovedAsync(application, administrator, [DeployingMachine.Disk(0)]);
        SequenceView sequence = await WindowsAsync();

        await administrator.AssignedAsync(linuxBoot.Id, sequence.Id);
        DeploymentSummary forWindowsPE = await administrator.AssignedAsync(windowsPE.Id, sequence.Id);

        Assert.Null((await linuxBoot.NextAsync()).Run);
        Assert.Equal(forWindowsPE.Id, Assert.IsType<AgentRun>((await windowsPE.NextAsync()).Run).Id);
    }

    [Fact]
    public async Task EachBootEnvironmentListsTheSequencesOfItsKindAtTheMachine()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine windowsPE = await DeployingMachine.SignedInAsync(application, operatorName, [DeployingMachine.Disk(0)]);
        using DeployingMachine linuxBoot = await DeployingMachine.SignedInAsync(application, operatorName, DeployingMachine.LinuxBoot());
        SequenceView linux = await LinuxAsync();
        SequenceView windows = await WindowsAsync();
        Guid[] both = [linux.Id, windows.Id];

        Assert.Equal([windows.Id], (await ChoicesAsync(windowsPE)).Select(choice => choice.Id).Where(both.Contains));
        Assert.Equal([linux.Id], (await ChoicesAsync(linuxBoot)).Select(choice => choice.Id).Where(both.Contains));
    }

    [Fact]
    public async Task TheLinuxBootEnvironmentPicksOnlyASequenceOfItsKind()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName, DeployingMachine.LinuxBoot());
        SequenceView linux = await LinuxAsync();
        SequenceView windows = await WindowsAsync();

        HttpResponseMessage refused = await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(windows.Id, 0, null));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            $"{windows.Name} runs in Windows PE, and this machine started the Linux boot environment. Choose one of the sequences the machine lists.",
            await TestDatabase.TitleAsync(refused));

        AgentRun picked = await RegisteredMachine.ReadAsync<AgentRun>(
            await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(linux.Id, 0, "LINUX-11")));

        Assert.Equal(DeploymentState.Assigned, picked.State);
        Assert.Equal(picked.Id, Assert.IsType<AgentRun>((await machine.NextAsync()).Run).Id);
    }

    // A rule only suggests, and the machine could not pick what it suggests.
    [Fact]
    public async Task ARulesSequenceIsSuggestedOnlyWhereItsBootEnvironmentStarted()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine windowsPE = await DeployingMachine.SignedInAsync(application, operatorName, [DeployingMachine.Disk(0)]);
        using DeployingMachine linuxBoot = await DeployingMachine.SignedInAsync(application, operatorName, DeployingMachine.LinuxBoot());
        SequenceView linux = await LinuxAsync();
        await administrator.CreatedRuleAsync(RuleRequests.MacRule(linux.Id, windowsPE.Registration.PrimaryMac));
        await administrator.CreatedRuleAsync(RuleRequests.MacRule(linux.Id, linuxBoot.Registration.PrimaryMac));

        AgentNextResult inWindowsPE = await windowsPE.NextAsync();
        AgentNextResult inLinuxBoot = await linuxBoot.NextAsync();

        Assert.True(inWindowsPE.CanPickSequence);
        Assert.Null(inWindowsPE.SuggestedSequenceId);
        Assert.Equal(linux.Id, inLinuxBoot.SuggestedSequenceId);
        Assert.True(Assert.Single(await ChoicesAsync(linuxBoot), choice => choice.Id == linux.Id).Suggested);
    }

    [Fact]
    public async Task TheLinuxBootEnvironmentReportsItsRunInItsOwnPhaseToTheEnd()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await ApprovedInLinuxBootAsync();
        SequenceView sequence = await LinuxAsync();
        DeploymentSummary assigned = await administrator.AssignedAsync(machine.Id, sequence.Id, "LINUX-12");
        AgentRun run = Assert.IsType<AgentRun>((await machine.NextAsync()).Run);
        SequenceStep write = run.Sequence.Steps[0];
        SequenceStep seed = run.Sequence.Steps[1];

        // The run's rows start in the phase the sequence starts in.
        Assert.All((await administrator.RunAsync(assigned.Id)).Steps, step => Assert.Equal(SequencePhase.LinuxBoot, step.Phase));

        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Running, []) with { Phase = SequencePhase.LinuxBoot, Activity = RunActivity.Preparing });
        await machine.ReportOkAsync(run.Id, Running(Step(write, StepState.Running)) with { Phase = SequencePhase.LinuxBoot });

        foreach (SequencePhase other in new[] { SequencePhase.WindowsPE, SequencePhase.Windows })
        {
            HttpResponseMessage refused = await machine.ReportAsync(run.Id, Running(Step(write, StepState.Done)) with { Phase = other });

            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal($"{PhaseRules.ReportRefusal(AgentEnvironment.LinuxBoot, other)} {AskAgain}", await TestDatabase.TitleAsync(refused));
        }

        await machine.ReportOkAsync(
            run.Id,
            Report(DeploymentState.Done, [Step(write, StepState.Done), Step(seed, StepState.Done)]) with { Phase = SequencePhase.LinuxBoot });

        DeploymentView done = await administrator.RunAsync(assigned.Id);
        Assert.Equal((DeploymentState.Done, SequencePhase.LinuxBoot), (done.Summary.State, done.Summary.Phase));
        Assert.All(done.Steps, step => Assert.Equal((StepState.Done, SequencePhase.LinuxBoot), (step.State, step.Phase)));
        Assert.Equal(MachineState.Done, (await application.MachineAsync(machine.Id)).State);
    }

    // A failure ends the run whatever phase it names, since the agent has stopped anyway.
    [Fact]
    public async Task WindowsPECannotReportTheLinuxBootPhaseUnlessTheRunFailed()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator, [DeployingMachine.Disk(0)]);
        DeploymentSummary assigned = await administrator.AssignedAsync(machine.Id, (await WindowsAsync()).Id);
        AgentRun run = Assert.IsType<AgentRun>((await machine.NextAsync()).Run);
        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Running, []) with { Activity = RunActivity.Preparing });

        HttpResponseMessage refused = await machine.ReportAsync(run.Id, Running(Step(run.Sequence.Steps[0], StepState.Running)) with { Phase = SequencePhase.LinuxBoot });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal($"{PhaseRules.ReportRefusal(AgentEnvironment.WindowsPE, SequencePhase.LinuxBoot)} {AskAgain}", await TestDatabase.TitleAsync(refused));

        await machine.ReportOkAsync(
            run.Id,
            Report(DeploymentState.Failed, []) with { Phase = SequencePhase.LinuxBoot, Error = "This machine started Windows PE." });

        Assert.Equal(DeploymentState.Failed, (await administrator.RunAsync(assigned.Id)).Summary.State);
    }
}
