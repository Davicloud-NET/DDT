// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Deployments;
using DDT.Contracts.Messages;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Values;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using DDT.Server.Sequences;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Rules;

// The sequence a machine gets, with a preview of the values a run would start with: of the run the machine has, or else
// of the sequence the rules choose, or of none, where only the machine, the rules, its roles and the defaults give values.
internal sealed class SequencePreviews(DdtDbContext database, SequenceResolver resolver, SequenceCatalog catalog, DdtSettings settings)
{
    // Null for a machine that is gone.
    public async Task<MachineSequenceResolution?> PreviewAsync(Guid machineId, CancellationToken cancellationToken)
    {
        Machine? machine = await database.Machines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == machineId, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return null;
        }

        SequenceResolution resolution = await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false);
        int problems = resolution.Sequence is { } sequence
            ? (await catalog.ViewAsync(sequence, cancellationToken).ConfigureAwait(false)).Problems.Count
            : 0;
        ServerMessage explanation = problems == 0 || resolution.Sequence is not { } chosen
            ? resolution.Explanation
            : ServerMessages.ResolutionCannotRun.With("explanation", resolution.Explanation, "sequence", chosen.Name, "count", problems);
        SequenceDefinition? definition = await DefinitionAsync(resolution, cancellationToken).ConfigureAwait(false);
        (IReadOnlyList<ResolvedValue> values, IReadOnlyList<ResolvedValue> inputDefaults, IReadOnlyList<SequenceProblem> valueProblems) =
            Values(machine, resolution, definition);

        return new MachineSequenceResolution(
            resolution.Source,
            resolution.Sequence?.Id,
            resolution.Sequence?.Name,
            resolution.Rule?.Id,
            problems,
            explanation.Text,
            explanation.Code,
            explanation.Args,
            resolution.Match.MatchedRuleIds,
            values,
            [.. definition?.Inputs ?? []],
            inputDefaults,
            valueProblems);
    }

    // The machine's run was given the definition frozen with it; otherwise the sequence the rules choose.
    private async Task<SequenceDefinition?> DefinitionAsync(SequenceResolution resolution, CancellationToken cancellationToken)
    {
        SequenceDefinition? definition = resolution.Sequence is { } chosen ? SequenceDocuments.Read(chosen.Definition) : null;

        if (resolution.Deployment is { } active
            && await database.DeploymentSnapshots
                .AsNoTracking()
                .Where(s => s.DeploymentId == active.Id)
                .Select(s => s.Definition)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false) is { } frozen)
        {
            definition = SequenceDocuments.Read(frozen);
        }

        return definition;
    }

    // A run that started works with the values it started with.
    private (IReadOnlyList<ResolvedValue> Values, IReadOnlyList<ResolvedValue> InputDefaults, IReadOnlyList<SequenceProblem> Problems) Values(
        Machine machine,
        SequenceResolution resolution,
        SequenceDefinition? definition)
    {
        if (resolution.Deployment is { State: DeploymentState.Running, Values: { } started })
        {
            return (JsonSerializer.Deserialize(started, DdtJsonContext.Default.IReadOnlyListResolvedValue) ?? [], [], []);
        }

        ValueResolution preview = ValueResolver.Resolve(MachineValues.Sources(
            machine,
            resolution,
            definition,
            MachineValues.Answers(RunAnswer.Read(resolution.Deployment?.Answers)),
            settings.Current.Deployment));

        return (preview.Values, preview.InputDefaults, MachineValues.ProblemsOf(preview));
    }
}
