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

// Roles and features, enabled in the installed Windows of a dry run.
[Trait("Category", "E2E")]
[Collection(DryRunCollection.Name)]
public sealed class FeatureDryRunTests(DryRunLab lab)
{
    // The agent's exit code for a run that finished.
    private const int Deployed = 4;

    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(3);

    // A dry run takes every feature as needing a restart, so the step restarts the machine once after the last and is
    // done in the turn after it.
    [Fact(Timeout = 600_000)]
    public async Task FeaturesThatNeedARestartRestartTheMachineOnceInsideTheStep()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        EnableFeaturesStep features = new()
        {
            Id = Guid.CreateVersion7(),
            Name = "Enable roles and features",
            Features = ["NetFx3", "TelnetClient"],
        };
        RunScriptStep after = DryRunTests.Script("After the features", SequencePhase.Windows, "echo after");
        SequenceView sequence = await lab.CreateSequenceAsync(
            "Features",
            [DryRunTests.Partition(), new ApplyImageStep { Id = Guid.CreateVersion7(), Name = "Apply the image", ImageId = lab.Image.Id }, features, after],
            cancellationToken);

        await using AgentProcess agent = lab.StartAgent();
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);
        await lab.ApproveAsync(machine.Id, null, cancellationToken);
        DeploymentSummary assigned = Assert.IsType<DeploymentSummary>((await lab.AssignAsync(machine.Id, new AssignSequenceRequest(sequence.Id, null), cancellationToken)).Deployment);

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
        DeploymentView run = await lab.RunAsync(assigned.Id, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));
        DeploymentStepView enabled = Assert.Single(run.Steps, step => step.StepId == features.Id);
        Assert.Equal((StepState.Done, SequencePhase.Windows, 1, (string?)null), (enabled.State, enabled.Phase, enabled.Restarts, enabled.Error));
        Assert.Equal(StepState.Done, Assert.Single(run.Steps, step => step.StepId == after.Id).State);

        Assert.Equal(1, agent.Output.Count("Dry run: NetFx3 is not enabled, and taken as enabled once the machine restarts."));
        Assert.Equal(1, agent.Output.Count("Dry run: TelnetClient is not enabled, and taken as enabled once the machine restarts."));
        Assert.Equal(1, agent.Output.Count("The machine restarts to finish the features, then the step goes on."));
        Assert.Equal(1, agent.Output.Count($"Step {features.Name} restarts the machine after"));
        Assert.Equal(1, agent.Output.Count("Features: 2 enabled."));
        Assert.Equal(1, agent.Output.Count($"Step {features.Name} finished after"));

        lab.AssertClean([agent], [], ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)));
    }
}
