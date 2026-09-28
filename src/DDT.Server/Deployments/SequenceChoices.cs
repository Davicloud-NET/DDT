// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.Values;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Rules;
using DDT.Server.Sequences;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// The sequences a technician signed in at the machine can choose from, and the one the rules suggest.
public sealed class SequenceChoices(
    DdtDbContext database,
    UserManager<DdtUser> users,
    SequenceCatalog catalog,
    SequenceResolver resolver,
    DdtSettings settings)
{
    // Someone who may deploy signed in at this machine in its current token generation, and nothing is assigned.
    public async Task<bool> CanPickAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        cancellationToken.ThrowIfCancellationRequested();

        if (machine.State is not (MachineState.Approved or MachineState.Failed)
            || machine.SignedInByUserId is not { } userId
            || machine.ActiveDeploymentId is not null)
        {
            return false;
        }

        DdtUser? user = await users.FindByIdAsync(userId.ToString("D")).ConfigureAwait(false);

        return user is { IsDisabled: false }
            && !await users.IsLockedOutAsync(user).ConfigureAwait(false)
            && (await users.IsInRoleAsync(user, DdtRoleNames.Operator).ConfigureAwait(false)
                || await users.IsInRoleAsync(user, DdtRoleNames.Administrator).ConfigureAwait(false));
    }

    // Only the sequences that can run, on this agent too, each with what it needs.
    public async Task<IReadOnlyList<AgentSequenceChoice>> ChoicesAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        List<TaskSequence> sequences = await database.TaskSequences.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        SequenceReferences references = await catalog.ReferencesAsync(cancellationToken).ConfigureAwait(false);
        SequenceResolution resolution = await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false);
        Guid? suggested = Suggested(resolution, references);
        DeploymentOptions deployment = settings.Current.Deployment;
        List<AgentSequenceChoice> choices = [];

        foreach (TaskSequence sequence in sequences.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id))
        {
            SequenceDefinition definition = SequenceDocuments.Read(sequence.Definition);

            if (definition.RequiredVersion() > machine.SequenceVersion || SequenceChecks.Check(definition, references).Problems.Count > 0)
            {
                continue;
            }

            // The console asks the inputs and the name after the pick, starting with what the machine, the rules and the
            // defaults give; a name typed there beats the one the values give.
            ValueResolution preview = ValueResolver.Resolve(MachineValues.Sources(machine, resolution, definition, null, deployment));
            choices.Add(Choice(sequence, definition, references, machine, preview) with { Suggested = sequence.Id == suggested });
        }

        return choices;
    }

    // The sequence the rules choose for the machine, offered first at the console. Only one that can run.
    public async Task<Guid?> SuggestedAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        SequenceReferences references = await catalog.ReferencesAsync(cancellationToken).ConfigureAwait(false);

        return Suggested(await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false), references);
    }

    private static AgentSequenceChoice Choice(
        TaskSequence sequence,
        SequenceDefinition definition,
        SequenceReferences references,
        Machine machine,
        ValueResolution preview)
    {
        IReadOnlyList<DeploymentArtifact> artifacts = RunSnapshots.Artifacts(Guid.Empty, definition, references, machine);
        AgentInput[] inputs =
        [
            .. (definition.Inputs ?? [])
                .OfType<InputDeclaration>()
                .Where(input => input.AskAt is InputAsk.Machine or InputAsk.Both)
                .Select(input => RunValues.Asked(input, preview)),
        ];
        bool needsName = SequenceChecks.ComputerNameUse(definition) is not null;

        return new AgentSequenceChoice(
            sequence.Id,
            sequence.Name,
            sequence.Description,
            SequenceChecks.Erases(definition),
            needsName,
            RunSnapshots.RequiredBytes(definition, artifacts),
            false,
            SequenceChecks.RawImage(definition, references)?.Name,
            SequenceChecks.RawImage(definition, references)?.BootCapability,
            SequenceChecks.RawImage(definition, references)?.SignedUnder,
            inputs.Length == 0 ? null : inputs,
            needsName && RunValues.NamesMachine(preview) ? preview.Effective[MachineVariableNames.ComputerName] : null);
    }

    private static Guid? Suggested(SequenceResolution resolution, SequenceReferences references) =>
        resolution is { Rule: not null, Sequence: { } sequence }
            && SequenceChecks.Check(SequenceDocuments.Read(sequence.Definition), references).Problems.Count == 0
                ? sequence.Id
                : null;
}
