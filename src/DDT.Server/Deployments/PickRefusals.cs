// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Server.Machines;

namespace DDT.Server.Deployments;

// Why a pick at the machine is refused over where runs start: the machine's run or the picked sequence starts elsewhere.
public sealed class PickRefusals(RunQueries queries, BootEnvironments environments)
{
    private const string HasRun = "This machine already has a run";

    // The machine has a run, so nothing is picked. An assigned run that starts in the other boot environment does not
    // start at the next poll, so the technician is told where it starts and whether the machine gets there.
    public async Task<string> HasRunAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Deployment? run = await queries.ActiveAsync(machine, cancellationToken).ConfigureAwait(false);
        AgentEnvironment needed = run is { State: DeploymentState.Assigned }
            ? await queries.StartsInAsync(run, cancellationToken).ConfigureAwait(false)
            : machine.AgentEnvironment;

        if (needed == machine.AgentEnvironment)
        {
            return $"{HasRun}. It starts once the agent asks the server again.";
        }

        string name = PhaseRules.Name(needed);

        return environments.Unreachable(machine, needed) is { } why
            ? $"{HasRun}, which runs in {name} and does not start in {PhaseRules.Name(machine.AgentEnvironment)}. {why}"
            : $"{HasRun}, which runs in {name}. The machine restarts into {name} once the agent asks the server again.";
    }

    // A run is only made where it starts. The machine restarts into the other boot environment first and picks there,
    // so nothing but its approval, its sign-in and the pick crosses the restart. Checked before the agent's version,
    // since a newer agent in this boot environment would not run the sequence either.
    public DeploymentDecision? OtherEnvironment(Machine machine, CheckedSequence sequence)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(sequence);

        if (PhaseRules.Starts(machine.AgentEnvironment, sequence.Definition))
        {
            return null;
        }

        AgentEnvironment needed = PhaseRules.StartsIn(sequence.Definition);
        string where = $"{sequence.Sequence.Name} runs in {PhaseRules.Name(needed)}, and this machine started {PhaseRules.Name(machine.AgentEnvironment)}.";

        return DeploymentDecision.Conflict(environments.ReachableFrom(machine) == needed
            ? $"{where} The machine restarts into {PhaseRules.Name(needed)} first. Choose the sequence again, and it does."
            : $"{where} Choose one of the sequences the machine lists.");
    }
}
