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
    DdtSettings settings,
    BootEnvironments environments)
{
    // True if someone who may deploy signed in at this machine in its current token generation, and nothing is
    // assigned.
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

    // Only the sequences that can run on this agent, each with what it needs and the boot environment that starts it.
    // Those of the other boot environment are among them when the machine can restart into it.
    public async Task<IReadOnlyList<AgentSequenceChoice>> ChoicesAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        List<TaskSequence> sequences = await database.TaskSequences.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        Listing listing = new(machine, await catalog.ReferencesAsync(cancellationToken).ConfigureAwait(false), environments.ReachableFrom(machine));
        SequenceResolution resolution = await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false);
        Guid? suggested = Suggested(listing, sequences.FirstOrDefault(sequence => sequence.Id == machine.PendingSequenceId), resolution);
        DeploymentOptions deployment = settings.Current.Deployment;
        List<AgentSequenceChoice> choices = [];

        foreach (TaskSequence sequence in sequences.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id))
        {
            SequenceDefinition definition = SequenceDocuments.Read(sequence.Definition);

            if (!listing.Lists(definition))
            {
                continue;
            }

            // The console asks the inputs and the name after the pick, prefilled with what the machine, the rules and
            // the defaults give. A name typed there beats the one from the values.
            ValueResolution preview = ValueResolver.Resolve(MachineValues.Sources(machine, resolution, definition, null, deployment));
            choices.Add(Choice(sequence, definition, listing.References, machine, preview) with
            {
                Suggested = sequence.Id == suggested,
                Environment = PhaseRules.StartsIn(definition),
            });
        }

        return choices;
    }

    // The sequence offered first at the console: the one picked at the machine before it changed its boot environment,
    // else the one the rules choose. Only one the machine lists.
    public async Task<Guid?> SuggestedAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Listing listing = new(machine, await catalog.ReferencesAsync(cancellationToken).ConfigureAwait(false), environments.ReachableFrom(machine));
        TaskSequence? pending = machine.PendingSequenceId is { } id
            ? await database.TaskSequences.AsNoTracking().FirstOrDefaultAsync(sequence => sequence.Id == id, cancellationToken).ConfigureAwait(false)
            : null;

        return Suggested(listing, pending, await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false));
    }

    // Whether the machine lists the sequence: its agent runs the version, and the sequence starts in the boot
    // environment the machine started, or in the one it can restart into.
    public static bool Offered(Machine machine, SequenceDefinition definition, AgentEnvironment? reachable)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(definition);

        AgentEnvironment starts = PhaseRules.StartsIn(definition);

        return definition.RequiredVersion() <= machine.SequenceVersion && (starts == machine.AgentEnvironment || starts == reachable);
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

    private static Guid? Suggested(Listing listing, TaskSequence? pending, SequenceResolution resolution)
    {
        TaskSequence? ruled = resolution is { Rule: not null, Sequence: { } sequence } ? sequence : null;

        return new[] { pending, ruled }
            .OfType<TaskSequence>()
            .FirstOrDefault(candidate => listing.Lists(SequenceDocuments.Read(candidate.Definition)))?.Id;
    }

    // What decides whether a machine lists a sequence. Reachable is the boot environment it can restart into, if any.
    private sealed record Listing(Machine Machine, SequenceReferences References, AgentEnvironment? Reachable)
    {
        public bool Lists(SequenceDefinition definition) =>
            Offered(Machine, definition, Reachable) && SequenceChecks.Check(definition, References).Problems.Count == 0;
    }
}
