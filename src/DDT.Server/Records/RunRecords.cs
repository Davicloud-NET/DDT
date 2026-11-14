// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Records;

// Takes what a run's Running step hands over, only from a step of the record's kind, so no other step and no other
// run can plant a key or a hash.
public sealed class RunRecords(DdtDbContext database, RecoveryKeyRecords keys, HardwareHashRecords hashes)
{
    // Null when the record is saved, or was saved before.
    public async Task<RecordRefusal?> SaveAsync(Machine machine, Guid runId, AgentRecord record, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(record);

        string kind = record is AgentRecoveryKey ? StepKindNames.EnableBitLocker : StepKindNames.RegisterAutopilot;

        if (await RefusalAsync(machine, runId, kind, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        return record switch
        {
            AgentRecoveryKey key => await keys.SaveAsync(machine, runId, key, actor, cancellationToken).ConfigureAwait(false),
            AgentHardwareHash hash => await hashes.SaveAsync(machine, hash, cancellationToken).ConfigureAwait(false),
            _ => new RecordRefusal(StatusCodes.Status400BadRequest, "This server takes no such record."),
        };
    }

    public async Task<(AgentHardwareHashStatus? Status, RecordRefusal? Refusal)> HardwareHashStatusAsync(
        Machine machine,
        Guid runId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (await RefusalAsync(machine, runId, StepKindNames.RegisterAutopilot, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return (null, refused);
        }

        return (await hashes.StatusAsync(machine.Id, cancellationToken).ConfigureAwait(false), null);
    }

    // The run has to be the machine's running run, with a Running row of this kind.
    private async Task<RecordRefusal?> RefusalAsync(Machine machine, Guid runId, string kind, CancellationToken cancellationToken)
    {
        bool running = machine.ActiveDeploymentId == runId && await database.Deployments
            .AnyAsync(d => d.Id == runId && d.State == DeploymentState.Running, cancellationToken)
            .ConfigureAwait(false);

        if (!running)
        {
            return new RecordRefusal(StatusCodes.Status409Conflict, "This machine has no such run that is running. Report the run as running first.");
        }

        bool stepRuns = await database.DeploymentSteps
            .AnyAsync(s => s.DeploymentId == runId && s.State == StepState.Running && s.Kind == kind, cancellationToken)
            .ConfigureAwait(false);

        return stepRuns
            ? null
            : new RecordRefusal(
                StatusCodes.Status409Conflict,
                kind == StepKindNames.EnableBitLocker
                    ? "Only a running Enable BitLocker step hands over a recovery key."
                    : "Only a running Register with Autopilot step hands over a hardware hash or asks about it.");
    }
}
