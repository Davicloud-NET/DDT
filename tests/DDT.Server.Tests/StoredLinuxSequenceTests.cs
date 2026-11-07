// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// Sequences that write a raw disk image, and their runs, as a server before version 4 stored them: at version 2 or 3,
// for Windows PE.
public sealed class StoredLinuxSequenceTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static RunScriptStep OldScript(ScriptInterpreter interpreter) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"Prepare with {interpreter}",
        Phase = SequencePhase.WindowsPE,
        Interpreter = interpreter,
        Script = "echo %DDT_PHASE%",
        RebootExitCodes = [],
    };

    // Stores the document the way a server of that version wrote it, which no request does any more.
    private async Task<SequenceView> StoredAsync(int version, params SequenceStep[] steps)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView created = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        string stored = SequenceRequests.Json(new SequenceDefinition(version, steps));

        await application.QueryAsync(async database =>
        {
            TaskSequence sequence = await database.TaskSequences.SingleAsync(s => s.Id == created.Id, TestContext.Current.CancellationToken);
            sequence.Definition = stored;

            return await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        return await RegisteredMachine.ReadAsync<SequenceView>(await administrator.GetAsync($"{SequenceRequests.Sequences}/{created.Id}"));
    }

    private async Task<SequenceStep[]> ImageAndSeedAsync()
    {
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");

        return [.. SequenceRequests.Linux(image.Id).Steps];
    }

    [Fact]
    public async Task AnImageWithItsSeedGoesOnRunningWithNoChange()
    {
        SequenceStep[] steps = await ImageAndSeedAsync();

        SequenceView flat = await StoredAsync(2, steps);
        SequenceView tree = await StoredAsync(3, new GroupStep { Id = Guid.NewGuid(), Name = "Install", Steps = steps });

        foreach (SequenceView view in new[] { flat, tree })
        {
            Assert.Empty(view.Problems);
            Assert.Equal(4, view.Definition.Version);
            Assert.NotNull(view.NodePhases);
            Assert.All(view.NodePhases, node => Assert.Equal([SequencePhase.LinuxBoot], node.Phases));
        }

        // Saved as it was read, it is stored at the version it needs.
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView saved = await RegisteredMachine.ReadAsync<SequenceView>(await administrator.SaveSequenceAsync(flat));
        Assert.Equal(4, saved.Definition.Version);
        Assert.Empty(saved.Problems);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AScriptWrittenForWindowsPEMakesItADraftThatSaysWhatToChange(int version)
    {
        SequenceStep[] steps = await ImageAndSeedAsync();
        RunScriptStep cmd = OldScript(ScriptInterpreter.Cmd);
        RunScriptStep powerShell = OldScript(ScriptInterpreter.PowerShell);

        SequenceView view = await StoredAsync(version, [cmd, .. steps, powerShell]);

        Assert.Equal(4, view.Definition.Version);
        Assert.Equal(
            [(cmd.Id, "phase", "sequence.windowsScriptInLinux"), (powerShell.Id, "phase", "sequence.windowsScriptInLinux")],
            view.Problems.Select(problem => (problem.StepId!.Value, problem.Field, problem.Code)));
        Assert.Equal(
            "This script was written for Windows PE, which no longer runs Linux sequences. Rewrite it for the shell and let it run in " +
            "the Linux boot environment.",
            view.Problems[0].Message);
        Assert.Equal(SequencePhase.WindowsPE, Assert.IsType<RunScriptStep>(view.Definition.Steps[0]).Phase);

        SignedInClient administrator = await application.AdministratorAsync();
        SequenceSummary summary = Assert.Single(
            await RegisteredMachine.ReadAsync<IReadOnlyList<SequenceSummary>>(await administrator.GetAsync(SequenceRequests.Sequences)),
            s => s.Id == view.Id);
        Assert.Equal(2, summary.ProblemCount);
    }

    // Rewritten for the shell, the same sequence runs again.
    [Fact]
    public async Task TheScriptRewrittenForTheShellMakesItRunAgain()
    {
        SequenceStep[] steps = await ImageAndSeedAsync();
        RunScriptStep cmd = OldScript(ScriptInterpreter.Cmd);
        SequenceView view = await StoredAsync(2, [cmd, .. steps]);
        SignedInClient administrator = await application.AdministratorAsync();

        RunScriptStep shell = cmd with { Phase = SequencePhase.LinuxBoot, Interpreter = ScriptInterpreter.Shell, Script = "echo \"$DDT_PHASE\"" };
        SequenceView saved = await RegisteredMachine.ReadAsync<SequenceView>(
            await administrator.SaveSequenceAsync(view, view.Definition with { Steps = [shell, .. steps] }));

        Assert.Empty(saved.Problems);
        Assert.Equal(4, saved.Definition.Version);
    }

    // A machine with an agent of version 3 in Windows PE, approved, with a Linux run assigned as before the upgrade:
    // its definition stored at version 2, and its rows in the Windows PE phase.
    private async Task<(DeployingMachine Machine, DeploymentSummary Run, SequenceStep[] Steps)> AssignedBeforeTheUpgradeAsync()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        DeployingMachine machine = await DeployingMachine.RegisterAsync(
            application,
            registration => registration with { SequenceVersion = 3, Disks = [DeployingMachine.Disk(0)] });
        (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        await machine.NextAsync();
        SequenceStep[] steps = await ImageAndSeedAsync();
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition(steps));
        DeploymentSummary run = await administrator.AssignedAsync(machine.Id, sequence.Id, "LINUX-20");

        await application.QueryAsync(async database =>
        {
            DeploymentSnapshot snapshot = await database.DeploymentSnapshots.SingleAsync(s => s.DeploymentId == run.Id, TestContext.Current.CancellationToken);
            snapshot.Definition = SequenceRequests.Json(new SequenceDefinition(2, steps));

            foreach (Deployments.DeploymentStep row in await database.DeploymentSteps.Where(s => s.DeploymentId == run.Id).ToListAsync(TestContext.Current.CancellationToken))
            {
                row.Phase = SequencePhase.WindowsPE;
            }

            return await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        return (machine, run, steps);
    }

    // Registers the machine again with its resume token, which keeps its approval and its run, and asks for its run.
    private static async Task<(AgentRun? Run, string Token)> NextFromAsync(DeployingMachine machine, AgentEnvironment environment)
    {
        AgentRegistration registration = machine.Registration with
        {
            ResumeToken = machine.ResumeToken,
            Environment = environment,
            SequenceVersion = SequenceDefinition.CurrentVersion,
        };
        AgentRegistrationResult registered = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(registration));
        AgentNextResult next = await RegisteredMachine.ReadAsync<AgentNextResult>(await machine.Agent.NextAsync(machine.Id, registered.Token!));

        return (next.Run, next.Token);
    }

    // Neither the agent from before the upgrade nor the server's agent in Windows PE gets the run. The Linux boot
    // environment does, at the version it needs now. A row takes the phase of the report that starts its step.
    [Fact]
    public async Task ARunAssignedBeforeTheUpgradeWaitsForTheLinuxBootEnvironment()
    {
        (DeployingMachine machine, DeploymentSummary assigned, SequenceStep[] steps) = await AssignedBeforeTheUpgradeAsync();
        using DeployingMachine _ = machine;
        SignedInClient administrator = await application.AdministratorAsync();

        Assert.Null((await machine.NextAsync()).Run);
        Assert.Null((await NextFromAsync(machine, AgentEnvironment.WindowsPE)).Run);

        (AgentRun? handed, string token) = await NextFromAsync(machine, AgentEnvironment.LinuxBoot);

        Assert.Equal(assigned.Id, Assert.IsType<AgentRun>(handed).Id);
        Assert.Equal(4, handed.Sequence.Version);
        Assert.All((await administrator.RunAsync(assigned.Id)).Steps, step => Assert.Equal(SequencePhase.WindowsPE, step.Phase));

        AgentRunReport[] reports =
        [
            Report(DeploymentState.Running, []) with { Activity = RunActivity.Preparing },
            Running(Step(steps[0], StepState.Running)),
            Running(Step(steps[0], StepState.Done), Step(steps[1], StepState.Running)),
            Report(DeploymentState.Done, [.. steps.Select(step => Step(step, StepState.Done))]),
        ];

        foreach (AgentRunReport report in reports)
        {
            (await machine.Agent.RunReportAsync(machine.Id, token, assigned.Id, report with { Phase = SequencePhase.LinuxBoot })).EnsureSuccessStatusCode();
        }

        DeploymentView view = await administrator.RunAsync(assigned.Id);
        Assert.Equal((DeploymentState.Done, SequencePhase.LinuxBoot), (view.Summary.State, view.Summary.Phase));
        Assert.All(view.Steps, step => Assert.Equal((StepState.Done, SequencePhase.LinuxBoot), (step.State, step.Phase)));
    }

    // The agent of version 3 got the run before the upgrade and writes the image from Windows PE while the server is
    // replaced. Its reports name the environment it registered from and its own phase, so they are taken to the end.
    [Fact]
    public async Task ARunAnOlderAgentStartedInWindowsPEBeforeTheUpgradeIsReportedToItsEnd()
    {
        (DeployingMachine machine, DeploymentSummary assigned, SequenceStep[] steps) = await AssignedBeforeTheUpgradeAsync();
        using DeployingMachine _ = machine;
        SignedInClient administrator = await application.AdministratorAsync();

        await machine.ReportOkAsync(assigned.Id, Report(DeploymentState.Running, []) with { Activity = RunActivity.Preparing });
        await machine.ReportOkAsync(assigned.Id, Running(Step(steps[0], StepState.Running)));
        await machine.ReportOkAsync(assigned.Id, Report(DeploymentState.Done, [.. steps.Select(step => Step(step, StepState.Done))]));

        DeploymentView view = await administrator.RunAsync(assigned.Id);
        Assert.Equal((DeploymentState.Done, SequencePhase.WindowsPE), (view.Summary.State, view.Summary.Phase));
        Assert.All(view.Steps, step => Assert.Equal((StepState.Done, SequencePhase.WindowsPE), (step.State, step.Phase)));
    }

    // A sequence stored before the upgrade is listed where the Linux boot environment started, and nowhere else.
    [Fact]
    public async Task ASequenceStoredAtVersion2IsOfferedInTheLinuxBootEnvironmentOnly()
    {
        string operatorName = await application.CreateUserAsync(Authentication.DdtRoleNames.Operator);
        SequenceView stored = await StoredAsync(2, await ImageAndSeedAsync());
        using DeployingMachine windowsPE = await DeployingMachine.SignedInAsync(application, operatorName, [DeployingMachine.Disk(0)]);
        using DeployingMachine linuxBoot = await DeployingMachine.SignedInAsync(application, operatorName, DeployingMachine.LinuxBoot());

        IReadOnlyList<AgentSequenceChoice> inWindowsPE =
            await RegisteredMachine.ReadAsync<IReadOnlyList<AgentSequenceChoice>>(await windowsPE.Agent.SequencesAsync(windowsPE.Id, windowsPE.Token));
        IReadOnlyList<AgentSequenceChoice> inLinuxBoot =
            await RegisteredMachine.ReadAsync<IReadOnlyList<AgentSequenceChoice>>(await linuxBoot.Agent.SequencesAsync(linuxBoot.Id, linuxBoot.Token));
        AgentRun picked = await RegisteredMachine.ReadAsync<AgentRun>(
            await linuxBoot.Agent.PickRunAsync(linuxBoot.Id, linuxBoot.Token, new AgentRunRequest(stored.Id, 0, "LINUX-21")));

        Assert.DoesNotContain(inWindowsPE, choice => choice.Id == stored.Id);
        Assert.Contains(inLinuxBoot, choice => choice.Id == stored.Id);
        Assert.Equal(4, picked.Sequence.Version);
    }

    // A sequence that installs Windows needs nothing of version 4 and keeps the version older agents run.
    [Fact]
    public async Task ASequenceThatInstallsWindowsKeepsItsVersion()
    {
        SequenceView view = await StoredAsync(1, SequenceRequests.ScriptOnly().Steps[0]);

        Assert.Equal(1, view.Definition.Version);
        Assert.Empty(view.Problems);
    }
}
