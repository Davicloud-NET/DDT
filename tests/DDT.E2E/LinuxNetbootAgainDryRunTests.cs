// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using Xunit;

namespace DDT.E2E;

// A machine starts from the network again after its Linux run was handed over, with the real host. While the installed
// Linux has the run, the boot environment asks the server, which changes nothing, and the run goes on. While it never
// took the run, the boot environment speaks for it with the token on the seed, and gives it up the third time.
[Trait("Category", "E2E")]
[Trait("Agent", "Linux")]
[Collection(DryRunCollection.Name)]
public sealed class LinuxNetbootAgainDryRunTests(DryRunLab lab)
{
    private const int Deployed = 4;

    private const string NeverTaken =
        "The machine started from the network 3 times after the run was handed over, and each time the run's token was still on " +
        "the cloud-init seed, so the installed Linux never took the run over. The image has no cloud-init or no systemd, the " +
        "user-data keeps cloud-init from running DDT's script, the machine does not start from its disk, or that script failed. " +
        "Look at the machine's own screen while it starts from its disk, and in the installed Linux at /var/log/cloud-init-output.log.";

    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(3);

    // The image and its seed in the boot environment, and one script in the installed Linux.
    private async Task<(SequenceView Sequence, RunScriptStep Installed)> SequenceAsync(string name, CancellationToken cancellationToken)
    {
        WriteRawImageStep write = new() { Id = Guid.CreateVersion7(), Name = "Write the disk image", ImageId = lab.RawImage.Id };
        WriteCloudInitSeedStep seed = new()
        {
            Id = Guid.CreateVersion7(),
            Name = "Write the cloud-init seed",
            MetaData = "local-hostname: \"{{ComputerName}}\"\n",
            UserData = "#cloud-config\nusers:\n  - default\n",
        };
        RunScriptStep installed = DryRunTests.Script("In the installed Linux", SequencePhase.Linux, "uname -a\n", ScriptInterpreter.Shell) with { RebootExitCodes = [194] };

        return (await lab.CreateSequenceAsync(name, [write, seed, installed], cancellationToken), installed);
    }

    // The machine, approved on the web and given the sequence. Returns it with its run's id.
    private async Task<(MachineSummary Machine, Guid RunId)> AssignAsync(AgentProcess agent, SequenceView sequence, CancellationToken cancellationToken)
    {
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);
        await lab.Live.WatchAsync(machine.Id, cancellationToken);
        await lab.ApproveAsync(machine.Id, null, cancellationToken);
        AssignSequenceRequest assignment = new(sequence.Id, $"E2E-{agent.DryRunId % 100_000:D5}");

        return (machine, (await lab.AssignAsync(machine.Id, assignment, cancellationToken)).Deployment!.Id);
    }

    private Task<MachineSummary> MachineWhenAsync(AgentProcess agent, string what, Func<MachineSummary, bool> holds, CancellationToken cancellationToken) =>
        Eventually.GetAsync(
            new Expectation($"{agent.SerialNumber} {what}", s_runTimeout, () => agent.Output.Tail()),
            async call => (await lab.Api.GetAsync("api/machines", DdtJsonContext.Default.IReadOnlyListMachineSummary, call))
                .FirstOrDefault(machine => machine.SerialNumber == agent.SerialNumber && holds(machine)),
            cancellationToken);

    // What ended a run when a machine registered from the network during it, before the boot environment looked first.
    [Fact(Timeout = 600_000)]
    public async Task AStartFromTheNetworkWhileTheInstalledLinuxHasTheRunDoesNotEndIt()
    {
        Assert.SkipWhen(lab.SkipReason is not null, lab.SkipReason ?? string.Empty);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (SequenceView sequence, _) = await SequenceAsync("Linux with a start from the network", cancellationToken);

        await using AgentProcess agent = lab.StartLinuxBootAgent("OnceInLinux");
        (MachineSummary machine, Guid runId) = await AssignAsync(agent, sequence, cancellationToken);

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));

        DeploymentView run = await lab.RunAsync(runId, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));
        Assert.All(run.Steps, step => Assert.Equal(StepState.Done, step.State));
        MachineSummary done = await lab.WaitForMachineAsync(agent, cancellationToken);
        Assert.Equal((machine.Id, MachineState.Done, AgentEnvironment.Linux), (done.Id, done.State, done.AgentEnvironment));

        // The boot environment was there twice and registered once. The second time it only asked, and was answered.
        Assert.Equal(2, agent.Output.Count("Dry run: the fake machine started the Linux boot environment"));
        Assert.Equal(1, agent.Output.Count("so the agent asks the server whether the run still goes on there."));
        Assert.Equal(1, agent.Output.Count($"The server says that run {runId:D} of this machine goes on in the installed Linux, and has changed nothing. This is start 1 of 5"));
        Assert.Equal(1, agent.Output.Count("The machine restarts into the installed Linux in 10 seconds."));
        Assert.Equal(1, agent.Output.Count("Registered as machine "));

        // The installed Linux then registered with the run's token, and the machine was never started over.
        IReadOnlyList<AuditEvent> audit = await lab.AuditAsync(runId, cancellationToken);
        Assert.Equal(1, audit.Count(entry => entry.Action == "deployment.resumed" && entry.Detail?.Contains(" from Linux with its run token.", StringComparison.Ordinal) == true));
        Assert.DoesNotContain(audit, entry => entry.Action == "deployment.failed");
        Assert.DoesNotContain(await lab.AuditAsync(machine.Id, cancellationToken), entry => entry.Action == "machine.reregistered");
        Assert.Equal(["meta-data", "user-data"], agent.SeedFiles());

        // The run's log on the server says that the machine came through the network.
        MachineLogPage log = await lab.LogAsync(machine.Id, $"deploymentId={runId:D}&limit=1000", cancellationToken);
        Assert.Single(log.Lines, line => line.Message.StartsWith("The machine started from the network while this run goes on in the installed Linux", StringComparison.Ordinal));

        lab.AssertClean(
            [agent],
            ["so the agent asks the server whether the run still goes on there."],
            ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)));
    }

    // A first start of the installed Linux that was cut short. The server counts the start from the network, and the
    // installed Linux then takes the run with the token the seed held all along.
    [Fact(Timeout = 600_000)]
    public async Task AStartFromTheNetworkBeforeTheInstalledLinuxTakesTheRunIsCountedAndTheRunStillEndsDone()
    {
        Assert.SkipWhen(lab.SkipReason is not null, lab.SkipReason ?? string.Empty);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (SequenceView sequence, _) = await SequenceAsync("Linux that comes back once", cancellationToken);

        await using AgentProcess agent = lab.StartLinuxBootAgent("OnceBeforeLinux");
        (MachineSummary machine, Guid runId) = await AssignAsync(agent, sequence, cancellationToken);
        MachineSummary once = await MachineWhenAsync(agent, "started from the network once", shown => shown.BootReturns == 1, cancellationToken);
        Assert.Equal((machine.Id, AgentEnvironment.LinuxBoot), (once.Id, once.AgentEnvironment));

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));

        DeploymentView run = await lab.RunAsync(runId, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));
        MachineSummary done = await lab.WaitForMachineAsync(agent, cancellationToken);
        Assert.Equal((MachineState.Done, AgentEnvironment.Linux), (done.State, done.AgentEnvironment));
        Assert.Null(done.BootReturns);
        IReadOnlyList<AuditEvent> audit = await lab.AuditAsync(runId, cancellationToken);
        Assert.Equal(1, audit.Count(entry => entry.Action == "deployment.resumed" && entry.Detail?.Contains(" from LinuxBoot with its run token.", StringComparison.Ordinal) == true));
        Assert.Equal(1, audit.Count(entry => entry.Action == "deployment.resumed" && entry.Detail?.Contains(" from Linux with its run token.", StringComparison.Ordinal) == true));
        Assert.DoesNotContain(await lab.AuditAsync(machine.Id, cancellationToken), entry => entry.Action == "deployment.run-token-refused");
        Assert.Equal(["meta-data", "user-data"], agent.SeedFiles());

        lab.AssertClean(
            [agent],
            ["The machine started from the network again before the installed Linux took the run over"],
            ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)));
    }

    // As with an image without cloud-init, or user-data that switches vendor data off: nothing ever calls from the disk.
    [Fact(Timeout = 600_000)]
    public async Task AnInstalledLinuxThatNeverTakesTheRunIsCountedShownAndGivenUpAtTheThirdStartFromTheNetwork()
    {
        Assert.SkipWhen(lab.SkipReason is not null, lab.SkipReason ?? string.Empty);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (SequenceView sequence, RunScriptStep installed) = await SequenceAsync("Linux that never calls", cancellationToken);

        await using AgentProcess agent = lab.StartLinuxBootAgent("Always");
        (MachineSummary machine, Guid runId) = await AssignAsync(agent, sequence, cancellationToken);

        // Each start from the network is on the machine's row while the boot agent waits its ten seconds.
        MachineSummary once = await MachineWhenAsync(agent, "started from the network once", shown => shown.BootReturns == 1, cancellationToken);
        Assert.Equal((MachineState.Deploying, AgentEnvironment.LinuxBoot), (once.State, once.AgentEnvironment));
        Assert.Equal((DeploymentState.Running, SequencePhase.Linux, RunActivity.Restarting), (once.Deployment?.State, once.Deployment?.Phase, once.Deployment?.Activity));
        Assert.Equal(once.Deployment?.UpdatedUtc, once.HandedOverUtc);
        MachineSummary twice = await MachineWhenAsync(agent, "started from the network twice", shown => shown.BootReturns == 2, cancellationToken);
        Assert.True(twice.HandedOverUtc > once.HandedOverUtc);

        MachineSummary failed = await MachineWhenAsync(agent, "had its run given up", shown => shown.State == MachineState.Failed, cancellationToken);
        Assert.Equal((machine.Id, AgentEnvironment.LinuxBoot), (failed.Id, failed.AgentEnvironment));
        Assert.Null(failed.BootReturns);
        Assert.Null(failed.HandedOverUtc);

        DeploymentView run = await lab.RunAsync(runId, cancellationToken);
        Assert.Equal((DeploymentState.Failed, NeverTaken, SequencePhase.Linux), (run.Summary.State, run.Summary.Error, run.Summary.Phase));
        Assert.Equal(StepState.Pending, Assert.Single(run.Steps, step => step.StepId == installed.Id).State);

        // The machine keeps its place: it registers again with the token of the registration before, and is not started over.
        await Eventually.WaitAsync(
            new Expectation($"Agent {agent.DryRunId}'s registration after the failure", s_runTimeout, () => agent.Output.Tail()),
            _ => Task.FromResult(agent.Output.Count("Registered as machine ") == 2),
            cancellationToken);
        Assert.Equal(MachineState.Failed, (await lab.WaitForMachineAsync(agent, cancellationToken)).State);
        IReadOnlyList<AuditEvent> audit = await lab.AuditAsync(runId, cancellationToken);
        Assert.Equal(3, audit.Count(entry => entry.Action == "deployment.resumed" && entry.Detail?.Contains(" from LinuxBoot with its run token.", StringComparison.Ordinal) == true));
        Assert.DoesNotContain(await lab.AuditAsync(machine.Id, cancellationToken), entry => entry.Action == "deployment.run-token-refused");

        // The server's log of the run has what the screen said, and the seed holds nothing of the run any more.
        MachineLogPage log = await lab.LogAsync(machine.Id, $"deploymentId={runId:D}&limit=1000", cancellationToken);
        Assert.Contains(log.Lines, line => line.Message.Contains("for the first time.", StringComparison.Ordinal));
        Assert.Contains(log.Lines, line => line.Message.Contains("for the second time.", StringComparison.Ordinal));
        Assert.Contains(log.Lines, line => line.Message == $"The run failed: {NeverTaken}");
        Assert.Equal(["meta-data", "user-data"], agent.SeedFiles());
        Assert.Equal(1, agent.Output.Count("What the hand-over put on the cloud-init seed of disk 0 is removed again"));

        lab.AssertClean(
            [agent],
            ["The machine started from the network again before the installed Linux took the run over", "The run failed: "],
            ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)));
    }
}
