// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Applications;

// An Applications input as the web, the agent and the console ask it: a MultiChoice of the offered entries still in the
// library, by id with their current names, the ticked ones as its default. The asked form is never stored or versioned,
// so a version check reads the definition as stored.
public static class ApplicationInputs
{
    public static SequenceDefinition Asked(SequenceDefinition definition, SequenceReferences references)
    {
        ArgumentNullException.ThrowIfNull(references);

        return Asked(definition, references.Applications, references.ApplicationBundles);
    }

    // Reads the library only for a definition with an Applications input.
    public static async Task<SequenceDefinition> AskedAsync(DdtDbContext database, SequenceDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(definition);

        if (!(definition.Inputs ?? []).Any(input => input is { Kind: InputKind.Applications }))
        {
            return definition;
        }

        List<Application> applications = await database.Applications.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        List<ApplicationBundle> bundles = await database.ApplicationBundles.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return Asked(definition, applications.ToDictionary(a => a.Id), bundles.ToDictionary(b => b.Id));
    }

    public static SequenceDefinition Asked(
        SequenceDefinition definition,
        IReadOnlyDictionary<Guid, Application> applications,
        IReadOnlyDictionary<Guid, ApplicationBundle> bundles)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(bundles);

        if (!(definition.Inputs ?? []).Any(input => input is { Kind: InputKind.Applications }))
        {
            return definition;
        }

        string? NameOf(ApplicationReference reference) => reference.Kind switch
        {
            ApplicationReferenceKind.Application => applications.GetValueOrDefault(reference.Id)?.Name,
            ApplicationReferenceKind.Bundle => bundles.GetValueOrDefault(reference.Id)?.Name,
            _ => null,
        };

        return definition with
        {
            Inputs = [.. (definition.Inputs ?? []).Select(input => input is { Kind: InputKind.Applications } ? Asked(input, NameOf) : input)],
        };
    }

    private static InputDeclaration Asked(InputDeclaration input, Func<ApplicationReference, string?> nameOf)
    {
        InputChoice[] choices =
        [
            .. (input.Offered ?? [])
                .OfType<ApplicationReference>()
                .Select(reference => nameOf(reference) is { } name ? new InputChoice(reference.Id.ToString("D"), name) : null)
                .OfType<InputChoice>()
                .DistinctBy(choice => choice.Value),
        ];
        HashSet<string> offered = [.. choices.Select(choice => choice.Value)];
        string ticked = string.Join(';', (input.Ticked ?? []).Select(id => id.ToString("D")).Where(offered.Contains).Distinct());

        return input with
        {
            Kind = InputKind.MultiChoice,
            Choices = choices,
            Default = ticked.Length > 0 ? ticked : null,
            MaxLength = null,
            Offered = null,
            Ticked = null,
        };
    }
}
