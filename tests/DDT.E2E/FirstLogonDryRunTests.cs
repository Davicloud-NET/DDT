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

// First-logon commands and the automatic logon: in the answer file without a Windows step, set up by the agent as a
// run in Windows ends otherwise.
[Trait("Category", "E2E")]
[Collection(DryRunCollection.Name)]
public sealed class FirstLogonDryRunTests(DryRunLab lab)
{
    // The agent's exit code for a run that finished.
    private const int Deployed = 4;

    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(3);

    private static WriteUnattendStep Unattend() => new()
    {
        Id = Guid.CreateVersion7(),
        Name = "Write the answer file",
        LocalAdministrator = true,
        FirstLogonCommands = ["cmd /c echo one", "cmd /c echo two"],
        AutoLogon = AutoLogonKind.LocalAdministrator,
        AutoLogonCount = 2,
    };

    private async Task<(AgentProcess Agent, DeploymentView Run)> RunAsync(string name, IReadOnlyList<SequenceStep> steps, CancellationToken cancellationToken)
    {
        SequenceView sequence = await lab.CreateSequenceAsync(name, steps, cancellationToken);
        AgentProcess agent = lab.StartAgent();
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);
        await lab.ApproveAsync(machine.Id, null, cancellationToken);
        DeploymentSummary assigned = Assert.IsType<DeploymentSummary>((await lab.AssignAsync(machine.Id, new AssignSequenceRequest(sequence.Id, null), cancellationToken)).Deployment);

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
        DeploymentView run = await lab.RunAsync(assigned.Id, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));

        return (agent, run);
    }

    [Fact(Timeout = 600_000)]
    public async Task ARunInWindowsSetsThemUpAsItEnds()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        (AgentProcess agent, DeploymentView run) = await RunAsync(
            "First logon in Windows",
            [
                DryRunTests.Partition(),
                new ApplyImageStep { Id = Guid.CreateVersion7(), Name = "Apply the image", ImageId = lab.Image.Id },
                Unattend(),
                DryRunTests.Script("In Windows", SequencePhase.Windows, "echo in Windows"),
            ],
            cancellationToken);

        await using (agent)
        {
            Assert.Equal(1, agent.Output.Count("local administrator yes, domain join no"));
            Assert.Equal(0, agent.Output.Count("automatic logon as"));
            Assert.Equal(1, agent.Output.Count("Dry run: in Windows, RunOnce would run cmd /c echo one"));
            Assert.Equal(1, agent.Output.Count("Dry run: in Windows, RunOnce would run cmd /c echo two"));
            Assert.Equal(1, agent.Output.Count("Dry run: in Windows, Winlogon would sign in as Admin 2 times."));
            Assert.Equal(1, agent.Output.Count("Set up 2 first-logon commands and the automatic logon of Admin, 2 times, from the next start of Windows."));

            lab.AssertClean([agent], [], ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)));
        }
    }

    [Fact(Timeout = 600_000)]
    public async Task WithoutAWindowsStepTheAnswerFileHoldsThem()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        (AgentProcess agent, DeploymentView run) = await RunAsync(
            "First logon in the answer file",
            [
                DryRunTests.Partition(),
                new ApplyImageStep { Id = Guid.CreateVersion7(), Name = "Apply the image", ImageId = lab.Image.Id },
                Unattend(),
            ],
            cancellationToken);

        await using (agent)
        {
            Assert.Equal(1, agent.Output.Count("local administrator yes, domain join no, automatic logon as Admin, 2 first-logon commands"));
            Assert.Equal(0, agent.Output.Count("RunOnce would run"));
            Assert.Equal(0, agent.Output.Count("Winlogon would sign in"));

            lab.AssertClean([agent], [], ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)));
        }
    }
}
