// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using DDT.Server.Data;
using Xunit;

namespace DDT.E2E;

// A dry run of a Linux sequence that goes on in the installed Linux, with the real host. On Linux the agent is the
// one for Linux, the static file a release ships. On Windows the agent for Windows stands in, which shares the library.
[Trait("Category", "E2E")]
[Trait("Agent", "Linux")]
[Collection(DryRunCollection.Name)]
public sealed class InstalledLinuxDryRunTests(DryRunLab lab)
{
    // The agent's exit code for a run that finished.
    private const int Deployed = 4;

    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(3);

    [Fact(Timeout = 600_000)]
    public async Task ARunGoesOnInTheInstalledLinuxThroughARestartSetsAVariableAndTheAgentRemovesItself()
    {
        Assert.SkipWhen(lab.SkipReason is not null, lab.SkipReason ?? string.Empty);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Steps steps = new(lab.RawImage.Id);
        SequenceView sequence = await lab.CreateSequenceAsync("Linux that goes on after its start", steps.Definition(), cancellationToken);
        Assert.Equal(
            [SequencePhase.LinuxBoot, SequencePhase.LinuxBoot, SequencePhase.Linux, SequencePhase.Linux, SequencePhase.Linux],
            sequence.StepPhases);

        await using AgentProcess agent = lab.StartLinuxBootAgent();
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);
        Assert.Equal((MachineState.Pending, AgentEnvironment.LinuxBoot), (machine.State, machine.AgentEnvironment));
        await lab.Live.WatchAsync(machine.Id, cancellationToken);
        Assert.Equal(MachineState.Approved, (await lab.ApproveAsync(machine.Id, null, cancellationToken)).State);
        string name = $"E2E-{agent.DryRunId % 100_000:D5}";
        DeploymentSummary assigned = (await lab.AssignAsync(machine.Id, new AssignSequenceRequest(sequence.Id, name), cancellationToken)).Deployment!;

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));

        DeploymentView run = await lab.RunAsync(assigned.Id, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));
        Assert.Equal(
            [
                (steps.Write.Id, StepState.Done, SequencePhase.LinuxBoot),
                (steps.Seed.Id, StepState.Done, SequencePhase.LinuxBoot),
                (steps.Restarts.Id, StepState.Done, SequencePhase.Linux),
                (steps.Note.Id, StepState.Done, SequencePhase.Linux),
                (steps.After.Id, StepState.Done, SequencePhase.Linux),
            ],
            run.Steps.Select(step => (step.StepId, step.State, step.Phase)));
        Assert.Equal($"yes on {name}", run.Variables!["Installed"]);

        // The same machine, which last called from the installed Linux, twice with the run's token: at its first
        // start and after the restart the script asked for.
        MachineSummary done = await lab.WaitForMachineAsync(agent, cancellationToken);
        Assert.Equal((machine.Id, MachineState.Done, AgentEnvironment.Linux), (done.Id, done.State, done.AgentEnvironment));
        IReadOnlyList<AuditEvent> audit = await lab.AuditAsync(assigned.Id, cancellationToken);
        Assert.Equal(2, audit.Count(entry => entry.Action == "deployment.resumed" && entry.Detail?.Contains(" from Linux with its run token.", StringComparison.Ordinal) == true));

        AssertWhatTheAgentSaid(agent);
        AssertTheDiskStaysAsItWasHandedOver(agent);
        lab.AssertClean([agent], [], ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)));
    }

    // Each of its systems once, the restart the script asked for, and its own removal.
    private static void AssertWhatTheAgentSaid(AgentProcess agent)
    {
        Assert.Equal(1, agent.Output.Count("The machine restarts into the installed Linux, where the agent goes on with the run."));
        Assert.Equal(1, agent.Output.Count("Dry run: the machine restarts into the installed Linux, which here starts its agent over there."));
        Assert.Equal(2, agent.Output.Count("the ddt-agent.service unit of the installed Linux."));
        Assert.Equal(1, agent.Output.Count("The script ended with exit code 0, which asks for a restart before the next step."));
        Assert.Equal(1, agent.Output.Count("Dry run: not run: systemctl reboot"));
        Assert.Equal(1, agent.Output.Count("Dry run: the machine restarts, which here starts its agent over."));
        Assert.Equal(1, agent.Output.Count("The run is over here, so the agent removes itself."));
        Assert.Equal(1, agent.Output.Count("Dry run: not run: systemctl disable ddt-agent.service"));
        Assert.False(Directory.Exists(agent.Root), $"The dry run's root {agent.Root} is still there.");
    }

    // With the agent's own file on the seed, which is megabytes of the published program.
    private static void AssertTheDiskStaysAsItWasHandedOver(AgentProcess agent)
    {
        using FileStream disk = new(agent.DiskPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] head = new byte[GptLayout.MaxHeadBytes];
        disk.ReadExactly(head);
        GptPartition partition = GptLayout.Read(head).Partitions.Single(candidate => candidate.Name == CloudInitSeed.Label);
        FatVolume volume = FatVolume.Open(disk, partition.FirstLba * GptLayout.SectorSize, (partition.LastLba - partition.FirstLba + 1) * GptLayout.SectorSize);

        Assert.Equal(["ddt-agent", "agent.json", "state.json", "token"], volume.List(LinuxAgentFiles.SeedFolder).Select(entry => entry.Name));
        Assert.True(volume.Find(LinuxAgentFiles.SeedAgent)!.Size > 1024 * 1024);
    }

    // The image and its seed in the boot environment, then a script that asks for a restart, a variable and a script
    // after both in the installed Linux.
    private sealed class Steps(Guid imageId)
    {
        public WriteRawImageStep Write { get; } = new() { Id = Guid.CreateVersion7(), Name = "Write the disk image", ImageId = imageId };

        public WriteCloudInitSeedStep Seed { get; } = new()
        {
            Id = Guid.CreateVersion7(),
            Name = "Write the cloud-init seed",
            MetaData = "local-hostname: \"{{ComputerName}}\"\n",
            UserData = "#cloud-config\nusers:\n  - default\n",
        };

        // A dry run takes every script as exit code 0, so that is this script's restart code, and its success another.
        public RunScriptStep Restarts { get; } = DryRunTests.Script("Install and restart", SequencePhase.Linux, "apt-get -y install something\n", ScriptInterpreter.Shell) with
        {
            SuccessExitCodes = [1],
            RebootExitCodes = [0],
        };

        public SetVariableStep Note { get; } = new() { Id = Guid.CreateVersion7(), Name = "Note it", Variable = "Installed", Value = "yes on {{ComputerName}}" };

        public RunScriptStep After { get; } = DryRunTests.Script("After the restart", SequencePhase.Linux, "echo \"$DDT_VAR_Installed\"\n", ScriptInterpreter.Shell) with
        {
            RebootExitCodes = [194],
        };

        public SequenceDefinition Definition() => new(SequenceDefinition.CurrentVersion, [Write, Seed, Restarts, Note, After])
        {
            Variables = [new VariableDeclaration { Name = "Installed", SetBySteps = true }],
        };
    }
}
