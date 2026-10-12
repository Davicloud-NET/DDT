// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Images;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// A run the Linux boot environment has handed over to the installed Linux, and the machine that holds its token.
internal static class HandedOverLinuxRun
{
    // What the boot agent reports when it sends the machine to its disk: no step moves, and a count if it has one.
    public static AgentRunReport Returned(int? returns) =>
        new(DeploymentState.Running, SequencePhase.Linux, [], null, 0, RunActivity.Restarting, null, BootReturns: returns);

    // The run as the machine's poll hands it out, before any step of it ran.
    public static async Task<(DeployingMachine Machine, AgentRun Run)> AssignedAsync(DdtApplication application, string name)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.LinuxWithInstalledSteps(image.Id));
        DeployingMachine machine = await DeployingMachine.RegisterAsync(application, DeployingMachine.LinuxBoot());
        (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        await machine.NextAsync();
        await administrator.AssignedAsync(machine.Id, sequence.Id, name);

        return (machine, Assert.IsType<AgentRun>((await machine.NextAsync()).Run));
    }

    public static async Task<(DeployingMachine Machine, AgentRun Run)> StartAsync(DdtApplication application, string name)
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync(application, name);
        StepRunState[] written = [Step(run.Sequence.Steps[0], StepState.Done), Step(run.Sequence.Steps[1], StepState.Done)];

        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Running, written) with { Phase = SequencePhase.LinuxBoot, Activity = RunActivity.HandingOver });
        await machine.ReportOkAsync(run.Id, Returned(null) with { Steps = written });

        return (machine, run);
    }

    // The agent's first call from the installed Linux, which takes the run over.
    public static async Task<AgentRegistrationResult> InstalledAsync(DeployingMachine machine) =>
        await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { RunToken = machine.RunToken, Environment = AgentEnvironment.Linux }));
}
