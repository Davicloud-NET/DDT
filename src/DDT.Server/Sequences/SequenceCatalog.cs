// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Server.Accounts;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Packages;
using DDT.Server.Rules;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Sequences;

// Reads stored sequences with their problems. The problems depend on the current library and settings.
public sealed class SequenceCatalog(DdtDbContext database, DdtSettings settings, AccountViews accounts)
{
    public async Task<SequenceReferences> ReferencesAsync(CancellationToken cancellationToken)
    {
        List<Image> images = await database.Images.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        List<Package> packages = await database.Packages.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        DeploymentOptions deployment = settings.Current.Deployment;
        List<string> ruleValues = await database.Rules.AsNoTracking().Select(rule => rule.Values).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<string> roleValues = await database.MachineRoles.AsNoTracking().Select(role => role.Values).ToListAsync(cancellationToken).ConfigureAwait(false);

        // A disabled rule counts too. It names the value on purpose, and it may be enabled again.
        HashSet<string> valueNames = new(
            [.. ruleValues.Concat(roleValues).SelectMany(RuleDocuments.ReadValues).Select(value => value.Name), .. MachineValues.DeploymentDefaultNames],
            StringComparer.OrdinalIgnoreCase);

        return new SequenceReferences(
            images.ToDictionary(i => i.Id),
            packages.ToDictionary(p => p.Id),
            !string.IsNullOrWhiteSpace(deployment.Domain.Name),
            !string.IsNullOrEmpty(deployment.LocalAdministrator.Password))
        {
            Accounts = await accounts.FactsAsync(cancellationToken).ConfigureAwait(false),
            ValueNames = valueNames,
        };
    }

    public async Task<SequenceView> ViewAsync(TaskSequence sequence, CancellationToken cancellationToken) =>
        View(sequence, await ReferencesAsync(cancellationToken).ConfigureAwait(false));

    public static SequenceView View(TaskSequence sequence, SequenceReferences references)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        SequenceDefinition definition = SequenceDocuments.Read(sequence.Definition);
        SequenceValidation validation = SequenceChecks.Check(definition, references);

        return new SequenceView(
            sequence.Id,
            sequence.Name,
            sequence.Description,
            sequence.Revision,
            definition,
            SequenceChecks.Phases(definition),
            validation.Problems,
            validation.Warnings,
            sequence.UpdatedUtc,
            sequence.UpdatedByName,
            SequenceChecks.NodePhases(definition));
    }

    public static SequenceSummary Summary(TaskSequence sequence, SequenceReferences references)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        SequenceDefinition definition = SequenceDocuments.Read(sequence.Definition);
        SequenceValidation validation = SequenceChecks.Check(definition, references);

        return new SequenceSummary(
            sequence.Id,
            sequence.Name,
            sequence.Description,
            sequence.Revision,
            SequenceTree.Leaves(definition).Count,
            validation.Problems.Count,
            validation.Warnings.Count,
            SequenceTree.Nodes(definition).Any(step => step.ErasesDisk),
            SequenceChecks.ComputerNameUse(definition) is not null,
            SequenceChecks.ContinuesInWindows(definition),
            sequence.UpdatedUtc,
            sequence.UpdatedByName,
            SequenceChecks.RawImage(definition, references)?.Name,
            SequenceChecks.RawImage(definition, references)?.BootCapability,
            SequenceChecks.RawImage(definition, references)?.SignedUnder);
    }
}
