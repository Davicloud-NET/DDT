// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// A Linux run goes on in the installed Linux. The boot environment starts it and hands it over, and the agent there
// reports it to its end. An agent that knows sequences up to version 4 is handed none with steps there.
public sealed class InstalledLinuxRunTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string AskAgain = "Ask the server for the current run.";

    // The version the agents of the Linux boot environment's first release say they run.
    private const int BootOnlyVersion = 4;

    private async Task<SequenceView> SequenceAsync(bool installedSteps = true)
    {
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");
        SequenceDefinition definition = installedSteps ? SequenceRequests.LinuxWithInstalledSteps(image.Id) : SequenceRequests.Linux(image.Id);

        return await (await application.AdministratorAsync()).CreatedSequenceAsync(definition);
    }

    // A machine that started the Linux boot environment and was approved on the web.
    private async Task<DeployingMachine> ApprovedInLinuxBootAsync(int sequenceVersion = SequenceDefinition.CurrentVersion)
    {
        DeployingMachine machine = await DeployingMachine.RegisterAsync(
            application,
            registration => DeployingMachine.LinuxBoot()(registration) with { SequenceVersion = sequenceVersion });
        (await (await application.AdministratorAsync()).PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        await machine.NextAsync();

        return machine;
    }

    // Registers the agent in the installed Linux with the run token the boot environment left it, and returns its token.
    private static async Task<AgentRegistrationResult> InstalledAsync(DeployingMachine machine) =>
        await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { RunToken = machine.RunToken, Environment = AgentEnvironment.Linux }));

    private static AgentRunReport InLinux(AgentRunReport report) => report with { Phase = SequencePhase.Linux };

    private static AgentRunReport InLinuxBoot(AgentRunReport report) => report with { Phase = SequencePhase.LinuxBoot };

    [Fact]
    public async Task StoresASequenceWithStepsInTheInstalledLinuxAtVersion5AndSaysWhereItGoesOn()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView installed = await SequenceAsync();
        SequenceView bootOnly = await SequenceAsync(installedSteps: false);

        Assert.Equal(5, installed.Definition.Version);
        Assert.Empty(installed.Problems);
        Assert.Equal([SequencePhase.LinuxBoot, SequencePhase.LinuxBoot, SequencePhase.Linux], installed.StepPhases);
        Assert.Equal(4, bootOnly.Definition.Version);

        IReadOnlyList<SequenceSummary> summaries =
            await RegisteredMachine.ReadAsync<IReadOnlyList<SequenceSummary>>(await administrator.GetAsync(SequenceRequests.Sequences));
        SequenceSummary continues = Assert.Single(summaries, summary => summary.Id == installed.Id);
        SequenceSummary ends = Assert.Single(summaries, summary => summary.Id == bootOnly.Id);

        Assert.Equal((SequenceKind.Linux, true, false), (continues.Kind, continues.ContinuesInLinux, continues.ContinuesInWindows));
        Assert.Equal((SequenceKind.Linux, false, false), (ends.Kind, ends.ContinuesInLinux, ends.ContinuesInWindows));
    }

    // A group and an IF have no phase of their own in the step list, so the list alone would not show it.
    [Fact]
    public async Task SaysThatASequenceGoesOnInTheInstalledLinuxWhenOnlyANestedStepRunsThere()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");
        GroupStep group = new() { Id = Guid.NewGuid(), Name = "Set up", Steps = [SequenceRequests.InLinux()] };
        IfStep branch = new()
        {
            Id = Guid.NewGuid(),
            Name = "If a laptop",
            Test = new TestCondition(MachineVariableNames.Model, ConditionOperator.Matches, "Latitude*"),
            Then = [],
            Else = [SequenceRequests.InLinux()],
        };
        SequenceView grouped = await administrator.CreatedSequenceAsync(SequenceRequests.LinuxWithInstalledSteps(image.Id, group));
        SequenceView branched = await administrator.CreatedSequenceAsync(SequenceRequests.LinuxWithInstalledSteps(image.Id, branch));

        IReadOnlyList<SequenceSummary> summaries =
            await RegisteredMachine.ReadAsync<IReadOnlyList<SequenceSummary>>(await administrator.GetAsync(SequenceRequests.Sequences));

        Assert.All([grouped, branched], view => Assert.Empty(view.Problems));
        Assert.All([grouped, branched], view => Assert.Equal(5, view.Definition.Version));
        Assert.True(Assert.Single(summaries, summary => summary.Id == grouped.Id).ContinuesInLinux);
        Assert.True(Assert.Single(summaries, summary => summary.Id == branched.Id).ContinuesInLinux);
    }

    [Fact]
    public async Task TheLinuxBootEnvironmentHandsTheRunOverAndTheInstalledLinuxReportsItToTheEnd()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await ApprovedInLinuxBootAsync();
        DeploymentSummary assigned = await administrator.AssignedAsync(machine.Id, (await SequenceAsync()).Id, "LINUX-30");
        AgentRun run = Assert.IsType<AgentRun>((await machine.NextAsync()).Run);
        StepRunState[] written = [Step(run.Sequence.Steps[0], StepState.Done), Step(run.Sequence.Steps[1], StepState.Done)];
        SequenceStep script = run.Sequence.Steps[2];

        // Each row starts in the phase its step runs in.
        Assert.Equal(
            [SequencePhase.LinuxBoot, SequencePhase.LinuxBoot, SequencePhase.Linux],
            (await administrator.RunAsync(assigned.Id)).Steps.Select(step => step.Phase));

        // The boot environment names the installed Linux around the hand-over, as Windows PE names Windows.
        await machine.ReportOkAsync(run.Id, InLinuxBoot(Report(DeploymentState.Running, []) with { Activity = RunActivity.Preparing }));
        await machine.ReportOkAsync(run.Id, InLinuxBoot(Report(DeploymentState.Running, written) with { Activity = RunActivity.HandingOver }));
        await machine.ReportOkAsync(run.Id, InLinux(Report(DeploymentState.Running, written) with { Activity = RunActivity.Restarting }));

        AgentRegistrationResult installed = await InstalledAsync(machine);
        Assert.Equal((MachineState.Deploying, run.Id), (installed.State, installed.RunId));
        Assert.Equal(AgentEnvironment.Linux, (await application.MachineAsync(machine.Id)).AgentEnvironment);

        async Task<HttpResponseMessage> SendAsync(AgentRunReport report) =>
            await machine.Agent.RunReportAsync(machine.Id, installed.Token!, run.Id, report);

        (await SendAsync(InLinux(Report(DeploymentState.Running, written) with { Activity = RunActivity.WaitingForCloudInit }))).EnsureSuccessStatusCode();
        Assert.Equal(
            (RunActivity.WaitingForCloudInit, SequencePhase.Linux),
            ((await administrator.RunAsync(assigned.Id)).Summary.Activity, (await administrator.RunAsync(assigned.Id)).Summary.Phase));

        // The installed Linux never takes the run back to the boot environment.
        HttpResponseMessage back = await SendAsync(InLinuxBoot(Running([.. written, Step(script, StepState.Running)])));
        Assert.Equal(HttpStatusCode.Conflict, back.StatusCode);
        Assert.Equal($"{PhaseRules.ReportRefusal(AgentEnvironment.Linux, SequencePhase.LinuxBoot)} {AskAgain}", await TestDatabase.TitleAsync(back));

        (await SendAsync(InLinux(Running([.. written, Step(script, StepState.Running)])))).EnsureSuccessStatusCode();
        (await SendAsync(InLinux(Report(DeploymentState.Done, [.. written, Step(script, StepState.Done)])))).EnsureSuccessStatusCode();

        DeploymentView done = await administrator.RunAsync(assigned.Id);
        Assert.Equal((DeploymentState.Done, SequencePhase.Linux), (done.Summary.State, done.Summary.Phase));
        Assert.Equal([SequencePhase.LinuxBoot, SequencePhase.LinuxBoot, SequencePhase.Linux], done.Steps.Select(step => step.Phase));
        Assert.Equal(MachineState.Done, (await application.MachineAsync(machine.Id)).State);
    }

    // Like the service in Windows: starting over would hand the boot environment's steps to a running Linux.
    [Fact]
    public async Task AnAgentInTheInstalledLinuxWithNothingToContinueChangesNothing()
    {
        using DeployingMachine machine = await ApprovedInLinuxBootAsync();
        Machine before = await application.MachineAsync(machine.Id);

        HttpResponseMessage refused = await machine.Agent.RegisterAsync(machine.Registration with { Environment = AgentEnvironment.Linux });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("This machine has no run for the DDT agent in the installed Linux to continue. The agent removes itself.", await TestDatabase.TitleAsync(refused));

        Machine after = await application.MachineAsync(machine.Id);
        Assert.Equal((before.State, before.TokenGeneration, before.AgentEnvironment), (after.State, after.TokenGeneration, after.AgentEnvironment));

        // Nor does an agent the server has never seen become a machine.
        using AgentClient stranger = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());
        AgentRegistration unknown = AgentClient.Registration(Guid.NewGuid().ToString("D"), "02AABBCCDD01") with { Environment = AgentEnvironment.Linux };
        Assert.Equal(HttpStatusCode.Conflict, (await stranger.RegisterAsync(unknown)).StatusCode);
        Assert.False(await application.QueryAsync(database => database.Machines.AnyAsync(m => m.SmbiosUuid == unknown.SmbiosUuid, TestContext.Current.CancellationToken)));
    }

    // A run starts in the boot environment of its kind, whatever version the agent in the installed Linux runs.
    [Fact]
    public async Task TheInstalledLinuxIsHandedNoRunThatHasNotStarted()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await ApprovedInLinuxBootAsync();
        DeploymentSummary assigned = await administrator.AssignedAsync(machine.Id, (await SequenceAsync()).Id, "LINUX-31");

        AgentRegistrationResult installed = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { ResumeToken = machine.ResumeToken, Environment = AgentEnvironment.Linux }));
        AgentNextResult next = await RegisteredMachine.ReadAsync<AgentNextResult>(await machine.Agent.NextAsync(machine.Id, installed.Token!));

        Assert.Null(next.Run);
        Assert.Null(next.RestartInto);
        Assert.False(next.CanPickSequence);
        Assert.Equal(DeploymentState.Assigned, (await administrator.RunAsync(assigned.Id)).Summary.State);

        // The boot environment gets it.
        AgentRegistrationResult boot = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { ResumeToken = next.ResumeToken, Environment = AgentEnvironment.LinuxBoot }));
        AgentNextResult inBoot = await RegisteredMachine.ReadAsync<AgentNextResult>(await machine.Agent.NextAsync(machine.Id, boot.Token!));

        Assert.Equal(assigned.Id, Assert.IsType<AgentRun>(inBoot.Run).Id);
    }

    // The run stays assigned, and the person at the machine reads why nothing starts.
    [Fact]
    public async Task AnAgentOfVersion4IsHandedNoRunWithStepsInTheInstalledLinuxAndIsToldWhy()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await ApprovedInLinuxBootAsync(BootOnlyVersion);
        SequenceView installed = await SequenceAsync();
        DeploymentSummary assigned = await administrator.AssignedAsync(machine.Id, installed.Id, "LINUX-34");

        AgentNextResult next = await machine.NextAsync();

        Assert.Null(next.Run);
        Assert.Null(next.RestartInto);
        Assert.Equal(
            $"{installed.Name} needs a newer agent than this machine runs, so it does not start. It stays assigned until the machine registers " +
            "with an agent that runs it. A machine started from the network takes the server's agent.",
            next.WaitReason);
        Assert.Equal(DeploymentState.Assigned, (await administrator.RunAsync(assigned.Id)).Summary.State);
        Assert.Equal(MachineState.Approved, (await application.MachineAsync(machine.Id)).State);

        // An agent that says 5 gets the same run.
        AgentRegistrationResult newer = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { ResumeToken = machine.ResumeToken, SequenceVersion = 5 }));
        AgentNextResult handed = await RegisteredMachine.ReadAsync<AgentNextResult>(await machine.Agent.NextAsync(machine.Id, newer.Token!));

        Assert.Equal(assigned.Id, Assert.IsType<AgentRun>(handed.Run).Id);
        Assert.Null(handed.WaitReason);
    }

    // What the first release of the Linux boot environment could run needs version 4 or less, as before.
    [Fact]
    public async Task AnAgentOfVersion4StillGetsARunThatEndsInTheLinuxBootEnvironment()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await ApprovedInLinuxBootAsync(BootOnlyVersion);
        DeploymentSummary assigned = await administrator.AssignedAsync(machine.Id, (await SequenceAsync(installedSteps: false)).Id, "LINUX-35");

        AgentNextResult next = await machine.NextAsync();
        AgentRun run = Assert.IsType<AgentRun>(next.Run);

        Assert.Equal((assigned.Id, 4), (run.Id, run.Sequence.Version));
        Assert.Null(next.WaitReason);
    }
}
