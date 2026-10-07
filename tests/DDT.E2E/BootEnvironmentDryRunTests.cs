// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using DDT.Server.Data;
using Xunit;

namespace DDT.E2E;

// Dry runs of a machine that gets from one boot environment into the other for its run, with the real host and the
// published agent. The agent leaves its resume token in the dry run's stand-in for the firmware, "restarts" and goes on
// as the other boot environment's agent. Nobody authorizes the machine a second time.
[Trait("Category", "E2E")]
[Collection(DryRunCollection.Name)]
public sealed class BootEnvironmentDryRunTests(DryRunLab lab)
{
    // The agent's exit code for a run that finished.
    private const int Deployed = 4;

    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(3);

    [Fact(Timeout = 600_000)]
    public async Task AMachineInWindowsPERestartsIntoTheLinuxBootEnvironmentAndWritesItsImageThere()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using BootEnvironmentFiles both = lab.OfferBothBootEnvironments();

        WriteRawImageStep write = new() { Id = Guid.CreateVersion7(), Name = "Write the disk image", ImageId = lab.RawImage.Id };
        WriteCloudInitSeedStep seed = new()
        {
            Id = Guid.CreateVersion7(),
            Name = "Write the cloud-init seed",
            MetaData = "local-hostname: \"{{ComputerName}}\"\n",
            UserData = "#cloud-config\n",
        };
        SequenceView sequence = await lab.CreateSequenceAsync("Linux after a restart", [write, seed], cancellationToken);

        await using AgentProcess agent = lab.StartAgent();
        MachineSummary machine = await AuthorizeAsync(agent, AgentEnvironment.WindowsPE, cancellationToken);
        AssignSequenceRequest assignment = new(sequence.Id, $"E2E-{agent.DryRunId % 100_000:D5}");
        DeploymentSummary assigned = (await lab.AssignAsync(machine.Id, assignment, cancellationToken)).Deployment!;

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));

        DeploymentView run = await lab.RunAsync(assigned.Id, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));
        Assert.All(run.Steps, step => Assert.Equal((StepState.Done, SequencePhase.LinuxBoot), (step.State, step.Phase)));
        await AssertTheSameAuthorizedMachineAsync(agent, machine, AgentEnvironment.LinuxBoot, cancellationToken);

        Assert.Equal(1, agent.Output.Count("The machine restarts into the Linux boot environment, where it stays signed in and goes on."));
        Assert.Equal(1, agent.Output.Count("Dry run: the machine restarts into the Linux boot environment, which here starts its agent over there."));
        Assert.Equal(1, agent.Output.Count("Dry run: the fake machine started the Linux boot environment"));

        using (FileStream disk = new(agent.DiskPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            byte[] head = new byte[GptLayout.MaxHeadBytes];
            disk.ReadExactly(head);
            Assert.Equal(["EFI", "root", CloudInitSeed.Label], GptLayout.Read(head).Partitions.Select(partition => partition.Name));
        }

        lab.AssertClean([agent], []);
    }

    [Fact(Timeout = 600_000)]
    public async Task AMachineInTheLinuxBootEnvironmentRestartsIntoWindowsPEAndInstallsWindowsThere()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using BootEnvironmentFiles both = lab.OfferBothBootEnvironments();

        ApplyImageStep apply = new() { Id = Guid.CreateVersion7(), Name = "Apply the image", ImageId = lab.Image.Id };
        SequenceView sequence = await lab.CreateSequenceAsync("Windows after a restart", [DryRunTests.Partition(), apply], cancellationToken);

        await using AgentProcess agent = lab.StartLinuxBootAgent();
        MachineSummary machine = await AuthorizeAsync(agent, AgentEnvironment.LinuxBoot, cancellationToken);
        DeploymentSummary assigned = (await lab.AssignAsync(machine.Id, new AssignSequenceRequest(sequence.Id, null), cancellationToken)).Deployment!;

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));

        DeploymentView run = await lab.RunAsync(assigned.Id, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));
        Assert.All(run.Steps, step => Assert.Equal((StepState.Done, SequencePhase.WindowsPE), (step.State, step.Phase)));
        await AssertTheSameAuthorizedMachineAsync(agent, machine, AgentEnvironment.WindowsPE, cancellationToken);

        Assert.Equal(1, agent.Output.Count("Dry run: the fake machine started the Linux boot environment"));
        Assert.Equal(1, agent.Output.Count("The machine restarts into Windows PE, where it stays signed in and goes on."));
        Assert.Equal(1, agent.Output.Count("Dry run: the machine restarts into Windows PE, which here starts its agent over there."));
        Assert.Equal(1, agent.Output.Count("Dry run: wimlib is not run. It would apply image 1 (DDT E2E Windows"));

        lab.AssertClean([agent], []);
    }

    // Approved on the web, like a machine nobody is standing at. The test watches it, so no push about its run is missed.
    private async Task<MachineSummary> AuthorizeAsync(AgentProcess agent, AgentEnvironment started, CancellationToken cancellationToken)
    {
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);

        Assert.Equal((MachineState.Pending, started), (machine.State, machine.AgentEnvironment));
        await lab.Live.WatchAsync(machine.Id, cancellationToken);
        Assert.Equal(MachineState.Approved, (await lab.ApproveAsync(machine.Id, null, cancellationToken)).State);

        return machine;
    }

    // The agent of the other boot environment registered as the machine that was approved, with the resume token it
    // found. A registration without it would have started the machine over, which the audit log would show.
    private async Task AssertTheSameAuthorizedMachineAsync(AgentProcess agent, MachineSummary machine, AgentEnvironment now, CancellationToken cancellationToken)
    {
        MachineSummary after = await lab.WaitForMachineAsync(agent, cancellationToken);
        IReadOnlyList<AuditEvent> audit = await lab.AuditAsync(machine.Id, cancellationToken);

        Assert.Equal((machine.Id, MachineState.Done, now), (after.Id, after.State, after.AgentEnvironment));
        Assert.Null(after.NextEnvironment);
        Assert.Equal(["machine.registered", "machine.approved"], audit.OrderBy(entry => entry.Id).Select(entry => entry.Action));
    }
}
