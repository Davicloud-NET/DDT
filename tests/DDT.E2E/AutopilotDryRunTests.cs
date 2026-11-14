// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.E2E;

// The hardware hash, read in the installed Windows of a dry run and kept by the server.
[Trait("Category", "E2E")]
[Collection(DryRunCollection.Name)]
public sealed class AutopilotDryRunTests(DryRunLab lab)
{
    // The agent's exit code for a run that finished.
    private const int Deployed = 4;

    // DryRunHardwareHashReader's stand-ins.
    private const string SerialNumber = "DRY-RUN-0001";

    private const string NoUpload = "The server is not set up to upload to Autopilot, so the step does not wait.";

    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(3);

    // The lab has no Autopilot settings, so the server uploads nothing and the step does not wait for it.
    [Fact(Timeout = 600_000)]
    public async Task TheServerKeepsTheHashTheStepHandsOver()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        RegisterAutopilotStep register = new()
        {
            Id = Guid.CreateVersion7(),
            Name = "Register with Autopilot",
            GroupTag = "Sales",
            WaitForImport = true,
        };
        SequenceView sequence = await lab.CreateSequenceAsync(
            "Autopilot",
            [DryRunTests.Partition(), new ApplyImageStep { Id = Guid.CreateVersion7(), Name = "Apply the image", ImageId = lab.Image.Id }, register],
            cancellationToken);

        await using AgentProcess agent = lab.StartAgent();
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);
        await lab.ApproveAsync(machine.Id, null, cancellationToken);
        DeploymentSummary assigned = Assert.IsType<DeploymentSummary>((await lab.AssignAsync(machine.Id, new AssignSequenceRequest(sequence.Id, null), cancellationToken)).Deployment);

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
        DeploymentView run = await lab.RunAsync(assigned.Id, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));
        DeploymentStepView step = Assert.Single(run.Steps, s => s.StepId == register.Id);
        Assert.Equal((StepState.Done, SequencePhase.Windows), (step.State, step.Phase));

        MachineRecordsView records = await lab.Api.GetAsync($"/api/machines/{machine.Id}/records", DdtJsonContext.Default.MachineRecordsView, cancellationToken);
        MachineHardwareHashView hash = Assert.IsType<MachineHardwareHashView>(records.HardwareHash);
        Assert.Equal((SerialNumber, "Sales", HardwareHashUploadState.None), (hash.SerialNumber, hash.GroupTag, hash.UploadState));

        Assert.Equal(1, agent.Output.Count($"Handed the hardware hash of serial number {SerialNumber} to the server."));
        Assert.Equal(1, agent.Output.Count(NoUpload));
        Assert.Equal(0, agent.Output.Count(Convert.ToBase64String(new byte[48])));

        lab.AssertClean([agent], [NoUpload], ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)));
    }
}
