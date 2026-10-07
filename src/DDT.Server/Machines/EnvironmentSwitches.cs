// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Sequences;

namespace DDT.Server.Machines;

// A machine says that it restarts into the other boot environment, for a sequence picked at it that starts there.
// Only the wish is recorded, with the sequence to offer first after the restart. No run is made and nothing is granted.
public sealed class EnvironmentSwitches(
    DdtDbContext database,
    SequenceChoices choices,
    NewRuns newRuns,
    BootEnvironments environments,
    MachineChangePublisher publisher)
{
    public async Task<EnvironmentSwitchOutcome> RequestAsync(
        Machine machine,
        AgentEnvironmentRequest request,
        string? address,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(request);

        // The same people who may pick a sequence there, since the restart is the first half of a pick.
        if (!await choices.CanPickAsync(machine, cancellationToken).ConfigureAwait(false))
        {
            return new EnvironmentSwitchOutcome(
                "Only an operator or administrator signed in at this machine can restart it into another boot environment. Sign in at the machine first.",
                MayNotPick: true);
        }

        if (environments.Unreachable(machine, request.Environment) is { } unreachable)
        {
            return new EnvironmentSwitchOutcome(unreachable);
        }

        TaskSequence? sequence = null;

        if (request.SequenceId is { } sequenceId)
        {
            sequence = await newRuns.LoadAsync(sequenceId, cancellationToken).ConfigureAwait(false);

            if (sequence is null)
            {
                return new EnvironmentSwitchOutcome("The sequence no longer exists. Choose another sequence.");
            }

            if (!PhaseRules.Starts(request.Environment, SequenceDocuments.Read(sequence.Definition)))
            {
                return new EnvironmentSwitchOutcome(
                    $"{sequence.Name} does not run in {PhaseRules.Name(request.Environment)}. It was probably changed after the list was shown. Choose it again.");
            }
        }

        machine.NextEnvironment = request.Environment;
        machine.NextEnvironmentUtc = now;
        machine.PendingSequenceId = sequence?.Id;
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.MachineEnvironmentRequested,
            machine.Id.ToString("D"),
            new Actor(machine.SignedInByUserId, machine.SignedInUserName, address, machine.Id),
            now,
            Detail(machine, request.Environment, sequence)));

        (bool saved, _) = await publisher.SaveDecisionAsync(machine, null, null, typeof(EnvironmentSwitches), cancellationToken).ConfigureAwait(false);

        return saved
            ? EnvironmentSwitchOutcome.Recorded
            : new EnvironmentSwitchOutcome("The machine changed while it was asked to restart. Ask the server again.");
    }

    private static string Detail(Machine machine, AgentEnvironment environment, TaskSequence? sequence)
    {
        string restart = $"Restarts from {PhaseRules.Name(machine.AgentEnvironment)} into {PhaseRules.Name(environment)}";

        return sequence is null
            ? $"{restart}, asked at the machine."
            : $"{restart} for {sequence.Name}, chosen at the machine. No run exists until it is chosen again there.";
    }
}
