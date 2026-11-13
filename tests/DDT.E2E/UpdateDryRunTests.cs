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

// Windows Update in the installed Windows of a dry run.
[Trait("Category", "E2E")]
[Collection(DryRunCollection.Name)]
public sealed class UpdateDryRunTests(DryRunLab lab)
{
    // The agent's exit code for a run that finished.
    private const int Deployed = 4;

    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(3);

    // The dry run offers two updates, then one, then none, each set needing a restart: three turns, two restarts.
    [Fact(Timeout = 600_000)]
    public async Task UpdatesRestartTheMachineTwiceInsideTheStepUntilTheSearchFindsNothing()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        WindowsUpdateStep updates = new() { Id = Guid.CreateVersion7(), Name = "Windows Update" };
        RunScriptStep after = DryRunTests.Script("After the updates", SequencePhase.Windows, "echo after");
        SequenceView sequence = await lab.CreateSequenceAsync(
            "Updates",
            [DryRunTests.Partition(), new ApplyImageStep { Id = Guid.CreateVersion7(), Name = "Apply the image", ImageId = lab.Image.Id }, updates, after],
            cancellationToken);

        await using AgentProcess agent = lab.StartAgent();
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);
        await lab.ApproveAsync(machine.Id, null, cancellationToken);
        DeploymentSummary assigned = Assert.IsType<DeploymentSummary>((await lab.AssignAsync(machine.Id, new AssignSequenceRequest(sequence.Id, null), cancellationToken)).Deployment);

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
        DeploymentView run = await lab.RunAsync(assigned.Id, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));
        DeploymentStepView installed = Assert.Single(run.Steps, step => step.StepId == updates.Id);
        Assert.Equal((StepState.Done, SequencePhase.Windows, 2, (string?)null), (installed.State, installed.Phase, installed.Restarts, installed.Error));
        Assert.Equal(StepState.Done, Assert.Single(run.Steps, step => step.StepId == after.Id).State);

        Assert.Equal(3, agent.Output.Count("made-up updates stand in for what it finds."));
        Assert.Equal(1, agent.Output.Count("Dry run: 2026-11 Cumulative Update for Windows 11 (KB5070001) is not installed"));
        Assert.Equal(1, agent.Output.Count("Dry run: Update for Microsoft Defender Antivirus antimalware platform (KB4052623) is not installed"));
        Assert.Equal(2, agent.Output.Count("The machine restarts to finish the updates, then the step searches again."));
        Assert.Equal(2, agent.Output.Count($"Step {updates.Name} restarts the machine after"));
        Assert.Equal(1, agent.Output.Count("Windows Update: 3 installed (2026-11 Cumulative Update for Windows 11 (KB5070001); 2026-11 Cumulative Update for .NET Framework (KB5070002); Update for Microsoft Defender Antivirus antimalware platform (KB4052623))."));
        Assert.Equal(1, agent.Output.Count($"Step {updates.Name} finished after"));

        lab.AssertClean([agent], [], ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)));
    }
}
