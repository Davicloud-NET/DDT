// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Images;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// Since when a machine's run waits for its installed system, as the machine list tells the web. Only the restart that
// leaves the boot environment for the installed system is a hand-over.
public sealed class HandedOverSinceTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private async Task<MachineSummary> ShownAsync(DeployingMachine machine) => Assert.Single(
        await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await (await application.AdministratorAsync()).GetAsync("/api/machines")),
        shown => shown.Id == machine.Id);

    // The web words this wait for Linux only. The server says it for Windows as well, from the same report.
    [Fact]
    public async Task AWindowsRunIsHandedOverFromWindowsPEsRestartUntilTheServiceInWindowsRegisters()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition(
        [
            .. SequenceRequests.Minimal(image.Id).Steps,
            new WriteUnattendStep { Id = Guid.NewGuid(), Name = "Answer file" },
            new RunScriptStep { Id = Guid.NewGuid(), Name = "In Windows", Phase = SequencePhase.Windows, Script = "hostname" },
        ]));
        DeploymentSummary assigned = await administrator.AssignedAsync(machine.Id, sequence.Id);
        AgentRun run = Assert.IsType<AgentRun>((await machine.NextAsync()).Run);
        StepRunState[] inWindowsPE = [.. run.Sequence.Steps.Take(3).Select(step => Step(step, StepState.Done))];

        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Running, inWindowsPE) with { Activity = RunActivity.HandingOver });
        Assert.Null((await ShownAsync(machine)).HandedOverUtc);

        await machine.ReportOkAsync(
            run.Id,
            Report(DeploymentState.Running, inWindowsPE) with { Phase = SequencePhase.Windows, Activity = RunActivity.Restarting });
        MachineSummary handedOver = await ShownAsync(machine);

        Assert.Equal(AgentEnvironment.WindowsPE, handedOver.AgentEnvironment);
        Assert.Equal((await administrator.RunAsync(assigned.Id)).Summary.UpdatedUtc, handedOver.HandedOverUtc);

        await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { RunToken = machine.RunToken, Environment = AgentEnvironment.Windows }));

        Assert.Null((await ShownAsync(machine)).HandedOverUtc);
    }

    // The machine comes back to the boot environment it left, and its agent there is known to call again.
    [Fact]
    public async Task ARestartInsideABootEnvironmentIsNoHandOver()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine inLinuxBoot = await DeployingMachine.RegisterAsync(application, DeployingMachine.LinuxBoot());
        (await administrator.PostAsync($"/api/machines/{inLinuxBoot.Id}/approve")).EnsureSuccessStatusCode();
        await inLinuxBoot.NextAsync();
        Image raw = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");
        SequenceView linux = await administrator.CreatedSequenceAsync(SequenceRequests.LinuxWithInstalledSteps(raw.Id));
        await administrator.AssignedAsync(inLinuxBoot.Id, linux.Id, "LINUX-47");
        AgentRun linuxRun = Assert.IsType<AgentRun>((await inLinuxBoot.NextAsync()).Run);

        await inLinuxBoot.ReportOkAsync(
            linuxRun.Id,
            Report(DeploymentState.Running, [Step(linuxRun.Sequence.Steps[0], StepState.Done)]) with
            {
                Phase = SequencePhase.LinuxBoot,
                Activity = RunActivity.Restarting,
            });

        using DeployingMachine inWindowsPE = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceView windows = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(image.Id));
        await administrator.AssignedAsync(inWindowsPE.Id, windows.Id);
        AgentRun windowsRun = Assert.IsType<AgentRun>((await inWindowsPE.NextAsync()).Run);

        await inWindowsPE.ReportOkAsync(
            windowsRun.Id,
            Running(Step(windowsRun.Sequence.Steps[0], StepState.Done)) with { Activity = RunActivity.Restarting });

        MachineSummary linuxShown = await ShownAsync(inLinuxBoot);
        MachineSummary windowsShown = await ShownAsync(inWindowsPE);

        Assert.Equal((RunActivity.Restarting, SequencePhase.LinuxBoot), (linuxShown.Deployment?.Activity, linuxShown.Deployment?.Phase));
        Assert.Null(linuxShown.HandedOverUtc);
        Assert.Equal((RunActivity.Restarting, SequencePhase.WindowsPE), (windowsShown.Deployment?.Activity, windowsShown.Deployment?.Phase));
        Assert.Null(windowsShown.HandedOverUtc);
    }
}
