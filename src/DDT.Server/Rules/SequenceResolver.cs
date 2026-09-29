// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Messages;
using DDT.Contracts.Rules;
using DDT.Core.Sequences;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Rules;

// Finds the sequence a machine gets. An assignment on the web or a choice at the machine comes first, then the first
// rule that chooses one. The rules are walked in every case, since they set values and give roles for whatever runs.
// Resolving authorizes nothing. An approval on the web runs a rule's sequence, and a console only offers it.
public sealed class SequenceResolver(DdtDbContext database)
{
    public async Task<SequenceResolution> ResolveAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        RuleBook book = await RuleBook.LoadAsync(database, cancellationToken).ConfigureAwait(false);

        return await ResolveAsync(machine, book, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SequenceResolution> ResolveAsync(Machine machine, RuleBook book, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(book);

        MachineVariables variables = MachineVariableReader.Read(machine);
        RuleMatch match = book.Match(variables, MachineValues.Own(machine));

        if (machine.ActiveDeploymentId is { } activeId
            && await database.Deployments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == activeId, cancellationToken).ConfigureAwait(false)
                is { } active)
        {
            object by = active.RequestedByName ?? (object)ServerMessages.SomeOperator.With();

            return active.Source == DeploymentSource.Console
                ? new SequenceResolution(
                    SequenceResolutionSource.Console,
                    null,
                    null,
                    active,
                    ServerMessages.ResolutionChosenAtMachine.With("by", by, "sequence", active.Title),
                    match,
                    variables)
                : new SequenceResolution(
                    SequenceResolutionSource.Assigned,
                    null,
                    null,
                    active,
                    ServerMessages.ResolutionAssignedOnWeb.With("by", by, "sequence", active.Title),
                    match,
                    variables);
        }

        // The foreign key keeps a rule's sequence, so it is there unless it was deleted a moment ago.
        TaskSequence? sequence = match.Chooser?.Rule.TaskSequenceId is { } sequenceId
            ? await database.TaskSequences.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sequenceId, cancellationToken).ConfigureAwait(false)
            : null;

        if (match.Chooser is not { } chooser || sequence is null)
        {
            return new SequenceResolution(
                SequenceResolutionSource.None,
                null,
                null,
                null,
                ServerMessages.ResolutionNoRuleChooses.With(),
                match,
                variables);
        }

        return new SequenceResolution(
            SequenceResolutionSource.Rule,
            sequence,
            chooser.Rule,
            null,
            ServerMessages.ResolutionRuleNumbered.With("number", chooser.Rule.Position + 1, "rule", chooser.Rule.Name, "sequence", sequence.Name),
            match,
            variables);
    }
}
