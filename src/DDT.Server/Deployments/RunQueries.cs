// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Sequences;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// The run a machine has, and the run as its agent receives it.
public sealed class RunQueries(DdtDbContext database, RunValues values, DdtSettings settings)
{
    public async Task<Deployment?> ActiveAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return machine.ActiveDeploymentId is { } id
            ? await database.Deployments.FindAsync([id], cancellationToken).ConfigureAwait(false)
            : null;
    }

    // What the Machines page shows: the active run, else the one created last.
    public async Task<Deployment?> ShownAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return (machine.ActiveDeploymentId ?? machine.LastDeploymentId) is { } id
            ? await database.Deployments.FindAsync([id], cancellationToken).ConfigureAwait(false)
            : null;
    }

    // One query for the whole list, which loads one run per machine.
    public async Task<IReadOnlyDictionary<Guid, Deployment>> ShownForAsync(
        IReadOnlyCollection<Machine> machines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machines);

        List<Guid> ids = [.. machines.Select(m => m.ActiveDeploymentId ?? m.LastDeploymentId).OfType<Guid>().Distinct()];

        List<Deployment> deployments = ids.Count == 0
            ? []
            : await database.Deployments
                .AsNoTracking()
                .Where(d => ids.Contains(d.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        return deployments.ToDictionary(d => d.MachineId);
    }

    // Null for an agent too old for the run's sequence, which would throw on a kind it does not know, and for the
    // service in Windows, which only ever continues a run that is running.
    public async Task<AgentRun?> HandOverAsync(Machine machine, Deployment run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(run);

        if (run.State is not (DeploymentState.Assigned or DeploymentState.Running)
            || (machine.AgentEnvironment == AgentEnvironment.Windows && run.State == DeploymentState.Assigned))
        {
            return null;
        }

        AgentRun handed = await AgentRunAsync(machine, run, cancellationToken).ConfigureAwait(false);

        return machine.SequenceVersion >= handed.Sequence.Version ? handed : null;
    }

    // The run as its agent receives it, from what was frozen when it was assigned.
    public async Task<AgentRun> AgentRunAsync(Machine machine, Deployment run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(run);

        SequenceDefinition definition = await DefinitionAsync(run, cancellationToken).ConfigureAwait(false);
        List<DeploymentArtifact> artifacts = await database.DeploymentArtifacts
            .AsNoTracking()
            .Where(a => a.DeploymentId == run.Id)
            .OrderBy(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return RunSnapshots.ForAgent(
            run,
            definition,
            artifacts,
            machine.AssignedName,
            await PendingInputsAsync(machine, run, definition, cancellationToken).ConfigureAwait(false));
    }

    // The inputs the machine asks before an assigned run can start: none unless a required one it asks has no answer,
    // and then every one it asks without an answer, so they are asked together.
    public async Task<IReadOnlyList<AgentInput>?> PendingInputsAsync(
        Machine machine,
        Deployment run,
        SequenceDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(definition);

        if (run.State != DeploymentState.Assigned || definition.Inputs is not { Count: > 0 })
        {
            return null;
        }

        RunValueCheck check = await values.CheckAsync(machine, run, definition, settings.Current.Deployment, cancellationToken).ConfigureAwait(false);

        return check.Missing.Any(input => input.AskAt is InputAsk.Machine or InputAsk.Both) ? check.AskedAtMachine : null;
    }

    // The step the machine's active run is running, anywhere in the run's tree, or why the step gets nothing.
    public async Task<(RunningStep? Running, string? Refusal)> RunningStepAsync(
        Machine machine,
        Guid runId,
        Guid stepId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Deployment? run = machine.ActiveDeploymentId == runId
            ? await database.Deployments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == runId, cancellationToken).ConfigureAwait(false)
            : null;

        if (run is not { State: DeploymentState.Running, Inputs: { } inputs })
        {
            return (null, "This machine has no such run that is running. Report the run as running first.");
        }

        DeploymentStep? row = await database.DeploymentSteps
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.DeploymentId == runId && s.StepId == stepId, cancellationToken)
            .ConfigureAwait(false);

        if (row is not { State: StepState.Running })
        {
            return (null, "Only a step that is running gets what it needs. Report the step as running first.");
        }

        SequenceDefinition definition = await DefinitionAsync(run, cancellationToken).ConfigureAwait(false);

        return (new RunningStep(run, SequenceTree.Index(definition).GetValueOrDefault(stepId)?.Step, RunInputs.Read(inputs), definition), null);
    }

    // The definition the run was given when it was assigned.
    public async Task<SequenceDefinition> DefinitionAsync(Deployment run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        DeploymentSnapshot snapshot = await database.DeploymentSnapshots
            .AsNoTracking()
            .SingleAsync(s => s.DeploymentId == run.Id, cancellationToken)
            .ConfigureAwait(false);

        return SequenceDocuments.Read(snapshot.Definition);
    }
}
