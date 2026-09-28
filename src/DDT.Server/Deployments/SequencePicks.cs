// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Rules;
using DDT.Server.Sequences;

namespace DDT.Server.Deployments;

// A sequence chosen at the machine by whoever signed in there, who is also recorded as having requested the run. A run of
// the sequence a rule suggested keeps the rule, for the history. Nothing here saves.
public sealed class SequencePicks(
    DdtDbContext database,
    NewRuns newRuns,
    SequenceChoices choices,
    SequenceResolver resolver,
    TimeProvider timeProvider)
{
    public async Task<DeploymentDecision> PickAsync(
        Machine machine,
        AgentRunRequest request,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(request);

        if (!await choices.CanPickAsync(machine, cancellationToken).ConfigureAwait(false))
        {
            return DeploymentDecision.Conflict(machine.ActiveDeploymentId is null
                ? "Only an operator or administrator signed in at this machine can choose a sequence. Start the machine from the network again and sign in."
                : "This machine already has a run. It starts once the agent asks the server again.");
        }

        TaskSequence? sequence = await newRuns.LoadAsync(request.SequenceId, cancellationToken).ConfigureAwait(false);

        if (sequence is null)
        {
            return DeploymentDecision.NotFound("The sequence no longer exists. Choose another sequence.");
        }

        CheckedSequence checkedSequence = await newRuns.CheckAsync(sequence, cancellationToken).ConfigureAwait(false);

        if (checkedSequence.Problem is { } problem)
        {
            return DeploymentDecision.Conflict(problem);
        }

        if (Refusal(machine, request, checkedSequence) is { } refusal)
        {
            return refusal;
        }

        SequenceResolution resolution = await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false);
        RunRequest run = new(
            machine,
            checkedSequence,
            request.Answers,
            request.ComputerName,
            request.AllowSecureBootMismatch,
            new RunCredentialGiver(machine.SignedInByUserId, machine.SignedInUserName, true),
            address)
        {
            Resolution = resolution,
        };

        return await CreateAsync(run, resolution, request.DiskNumber, cancellationToken).ConfigureAwait(false);
    }

    // What the list the agent showed no longer fits: the sequence was changed after it was shown.
    private static DeploymentDecision? Refusal(Machine machine, AgentRunRequest request, CheckedSequence sequence)
    {
        if (sequence.Definition.RequiredVersion() > machine.SequenceVersion)
        {
            return DeploymentDecision.Conflict(
                $"{sequence.Sequence.Name} needs a newer agent than this machine runs. Start the machine from the network again, so it gets the " +
                "server's agent, and choose it then.");
        }

        // The agent sends a disk for every sequence it listed as erasing one, once the technician typed ERASE. It refuses a
        // run without one itself, but only if the answer to this pick reached it and told it the run's id.
        if (SequenceChecks.Erases(sequence.Definition) && request.DiskNumber is null)
        {
            return DeploymentDecision.Conflict(
                $"{sequence.Sequence.Name} erases a disk, and no disk was chosen for it at the machine. It was probably changed after the list was shown. Choose it again.");
        }

        return SequenceChecks.Erases(sequence.Definition) && request.DiskNumber is < 0
            ? DeploymentDecision.Invalid("diskNumber", "Choose one of the disks the agent listed.")
            : null;
    }

    private async Task<DeploymentDecision> CreateAsync(
        RunRequest request,
        SequenceResolution resolution,
        int? diskNumber,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        GivenAnswers given = await newRuns.GivenAsync(request, now, cancellationToken).ConfigureAwait(false);
        (bool allowMismatch, DeploymentDecision? invalid) = NewRuns.Validate(request, given);

        if (invalid is not null)
        {
            return invalid;
        }

        (Machine machine, CheckedSequence sequence) = (request.Machine, request.Sequence);
        NewRun run = new(request, DeploymentSource.Console, given, now)
        {
            RuleId = resolution.Sequence?.Id == sequence.Sequence.Id ? resolution.Rule?.Id : null,
            DiskNumber = SequenceChecks.Erases(sequence.Definition) ? diskNumber : null,
            AllowSecureBootMismatch = allowMismatch,
        };
        (Deployment deployment, DeploymentDecision? refused) = await newRuns.CreateAsync(run, cancellationToken).ConfigureAwait(false);

        if (refused is not null)
        {
            return refused;
        }

        machine.State = MachineState.Approved;
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.DeploymentAssigned,
            deployment.Id.ToString("D"),
            new Actor(machine.SignedInByUserId, machine.SignedInUserName, request.Address, machine.Id),
            now,
            $"{sequence.Sequence.Name}, revision {sequence.Sequence.Revision}, to machine {machine.Id:D}, chosen at the machine."
                + SecureBootPolicy.MismatchNote(machine, sequence.Definition, sequence.References, allowMismatch)));

        return DeploymentDecision.Accepted(deployment);
    }
}
