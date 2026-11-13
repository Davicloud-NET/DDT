// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Core.Applications;
using DDT.Server.Applications;

namespace DDT.Server.Deployments;

// Reads a value's ; list of applications and bundles. Each entry is an id, as an Applications input answers, or a name,
// as a rule writes it.
public static class ApplicationTokens
{
    // Unknown is the first entry the library does not have; the references are then incomplete.
    public static (IReadOnlyList<ApplicationReference> References, string? Unknown) Read(string value, ApplicationLibrary library)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(library);

        Dictionary<string, ApplicationReference> byName = new(StringComparer.Ordinal);

        foreach (ApplicationBundle bundle in library.Bundles.Values)
        {
            byName.TryAdd(bundle.NormalizedName, new ApplicationReference(bundle.Id, ApplicationReferenceKind.Bundle));
        }

        foreach (Application application in library.Applications.Values)
        {
            byName.TryAdd(application.NormalizedName, new ApplicationReference(application.Id, ApplicationReferenceKind.Application));
        }

        List<ApplicationReference> references = [];

        foreach (string token in value.Split(ApplicationNames.Separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            ApplicationReference? reference = ById(token, library)
                ?? (ApplicationNames.Normalize(token) is { } name ? byName.GetValueOrDefault(name) : null);

            if (reference is null)
            {
                return (references, token);
            }

            if (!references.Contains(reference))
            {
                references.Add(reference);
            }
        }

        return (references, null);
    }

    private static ApplicationReference? ById(string token, ApplicationLibrary library)
    {
        if (!Guid.TryParseExact(token, "D", out Guid id))
        {
            return null;
        }

        return library.Applications.ContainsKey(id) ? new ApplicationReference(id, ApplicationReferenceKind.Application)
            : library.Bundles.ContainsKey(id) ? new ApplicationReference(id, ApplicationReferenceKind.Bundle)
            : null;
    }
}
