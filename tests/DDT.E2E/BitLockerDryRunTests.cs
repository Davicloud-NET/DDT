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

// BitLocker pre-provisioned in Windows PE and enabled in the installed Windows of a dry run, which ends with the
// recovery key on the server and nowhere else.
[Trait("Category", "E2E")]
[Collection(DryRunCollection.Name)]
public sealed class BitLockerDryRunTests(DryRunLab lab)
{
    // The agent's exit code for a run that finished.
    private const int Deployed = 4;

    // DryRunBitLocker's stand-ins.
    private const string Password = "123321-234432-345543-456654-567765-678876-112211-221122";
    private static readonly Guid s_protectorId = Guid.Parse("4a5359b5-b088-431b-91e0-c915fee4f6b4");

    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(3);

    [Fact(Timeout = 600_000)]
    public async Task TheServerKeepsTheRecoveryKeyTheStepHandsOver()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        PreprovisionBitLockerStep preprovision = new() { Id = Guid.CreateVersion7(), Name = "Pre-provision BitLocker", IfUnavailable = BitLockerUnavailable.Fail };
        EnableBitLockerStep enable = new() { Id = Guid.CreateVersion7(), Name = "Enable BitLocker", WaitForEncryption = true };
        SequenceView sequence = await lab.CreateSequenceAsync(
            "BitLocker",
            [DryRunTests.Partition(), preprovision, new ApplyImageStep { Id = Guid.CreateVersion7(), Name = "Apply the image", ImageId = lab.Image.Id }, enable],
            cancellationToken);

        await using AgentProcess agent = lab.StartAgent();
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);
        await lab.ApproveAsync(machine.Id, null, cancellationToken);
        DeploymentSummary assigned = Assert.IsType<DeploymentSummary>((await lab.AssignAsync(machine.Id, new AssignSequenceRequest(sequence.Id, null), cancellationToken)).Deployment);

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
        DeploymentView run = await lab.RunAsync(assigned.Id, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));
        Assert.Equal(
            [(preprovision.Id, StepState.Done, SequencePhase.WindowsPE), (enable.Id, StepState.Done, SequencePhase.Windows)],
            run.Steps.Where(s => s.StepId == preprovision.Id || s.StepId == enable.Id).Select(s => (s.StepId, s.State, s.Phase)));

        MachineRecordsView records = await lab.Api.GetAsync($"/api/machines/{machine.Id}/records", DdtJsonContext.Default.MachineRecordsView, cancellationToken);
        RecoveryKeyView key = Assert.Single(records.RecoveryKeys);
        Assert.Equal((s_protectorId, "C:", assigned.Id, (DateTimeOffset?)null), (key.ProtectorId, key.Volume, key.RunId, key.ReplacedUtc));

        RecoveryKeyReveal revealed = await lab.Api.SendAsync(
            HttpMethod.Post,
            $"/api/machines/{machine.Id}/recovery-keys/{key.Id}/reveal",
            DdtJsonContext.Default.RecoveryKeyReveal,
            cancellationToken);
        Assert.Equal(Password, revealed.Password);

        Assert.Equal(1, agent.Output.Count("The server has the recovery password of protector"));
        string detail = JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView);
        Assert.All(
            [agent.Output.Text, lab.Host.Output.Text, lab.Live.Traffic, detail, JsonSerializer.Serialize(records, DdtJsonContext.Default.MachineRecordsView)],
            text => Assert.DoesNotContain(Password, text, StringComparison.Ordinal));

        lab.AssertClean([agent], [], ("The run's detail", detail));
    }
}
