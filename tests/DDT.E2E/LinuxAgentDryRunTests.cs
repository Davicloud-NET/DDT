// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using Xunit;

namespace DDT.E2E;

// A dry run with the agent for Linux itself, the static file a release ships, against the real host on Linux. The
// other classes run Linux sequences with the agent for Windows standing in, which shares the library but not the file.
[Trait("Category", "E2E")]
[Trait("Agent", "Linux")]
[Collection(DryRunCollection.Name)]
public sealed class LinuxAgentDryRunTests(DryRunLab lab)
{
    // The agent's exit code for a run that finished.
    private const int Deployed = 4;

    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(3);

    [Fact(Timeout = 600_000)]
    public async Task TheAgentForLinuxRegistersAsTheLinuxBootEnvironmentAndWritesAnImageWithItsSeed()
    {
        lab.SkipUnlessLinuxAgent();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        // The boot environment can't restart in such a sequence, so the script treats no exit code as a restart.
        RunScriptStep before = DryRunTests.Script("Before the disk", SequencePhase.LinuxBoot, "echo \"$DDT_PHASE\"", ScriptInterpreter.Shell) with
        {
            RebootExitCodes = [],
        };
        WriteRawImageStep write = new() { Id = Guid.CreateVersion7(), Name = "Write the disk image", ImageId = lab.RawImage.Id };
        WriteCloudInitSeedStep seed = new()
        {
            Id = Guid.CreateVersion7(),
            Name = "Write the cloud-init seed",
            MetaData = "local-hostname: \"{{ComputerName}}\"\n",
            UserData = "#cloud-config\n",
        };
        SequenceView sequence = await lab.CreateSequenceAsync("Linux with its own agent", [before, write, seed], cancellationToken);
        Assert.All(sequence.StepPhases, phase => Assert.Equal(SequencePhase.LinuxBoot, phase));

        await using AgentProcess agent = lab.StartAgent();
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);

        Assert.Equal((MachineState.Pending, AgentEnvironment.LinuxBoot), (machine.State, machine.AgentEnvironment));
        Assert.Matches(@"^\d+\.\d+\.\d+$", machine.AgentVersion);
        await lab.Live.WatchAsync(machine.Id, cancellationToken);
        Assert.Equal(MachineState.Approved, (await lab.ApproveAsync(machine.Id, null, cancellationToken)).State);
        AssignSequenceRequest assignment = new(sequence.Id, $"E2E-{agent.DryRunId % 100_000:D5}");
        DeploymentSummary assigned = (await lab.AssignAsync(machine.Id, assignment, cancellationToken)).Deployment!;

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));

        DeploymentView run = await lab.RunAsync(assigned.Id, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));
        Assert.Equal(
            [(before.Id, StepState.Done), (write.Id, StepState.Done), (seed.Id, StepState.Done)],
            run.Steps.Select(step => (step.StepId, step.State)));
        Assert.All(run.Steps, step => Assert.Equal(SequencePhase.LinuxBoot, step.Phase));
        Assert.Equal(MachineState.Done, (await lab.WaitForMachineAsync(agent, cancellationToken)).State);

        Assert.Equal(1, agent.Output.Count("Dry run: the fake machine started the Linux boot environment"));
        Assert.Equal(1, agent.Output.Count("Not checking for a newer agent in a dry run or an agent that was not published."));
        Assert.Equal(1, agent.Output.Count("and taken as exit code 0: /bin/sh "));

        using (FileStream disk = new(agent.DiskPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            byte[] head = new byte[GptLayout.MaxHeadBytes];
            disk.ReadExactly(head);
            Assert.Equal(["EFI", "root", CloudInitSeed.Label], GptLayout.Read(head).Partitions.Select(partition => partition.Name));
        }

        lab.AssertClean([agent], []);
    }
}
