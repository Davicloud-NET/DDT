// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Server.Sequences;

namespace DDT.Server.Applications;

// What names each application and bundle: the bundles that hold it, the applications that depend on it and the
// sequences whose steps or inputs name it, in that order, each by name.
internal static class ApplicationUses
{
    public static Dictionary<Guid, IReadOnlyList<ApplicationUse>> Of(
        IEnumerable<Application> applications,
        IEnumerable<ApplicationBundle> bundles,
        IEnumerable<TaskSequence> sequences)
    {
        IEnumerable<(Guid Used, ApplicationUse By)> byBundles = bundles.SelectMany(bundle =>
            ApplicationColumns.ReadIds(bundle.Members).Select(member => (member, new ApplicationUse(bundle.Id, bundle.Name, ApplicationUseKind.Bundle))));
        IEnumerable<(Guid Used, ApplicationUse By)> byApplications = applications.SelectMany(application =>
            ApplicationColumns.ReadIds(application.DependsOn)
                .Select(dependency => (dependency, new ApplicationUse(application.Id, application.Name, ApplicationUseKind.Application))));
        IEnumerable<(Guid Used, ApplicationUse By)> bySequences = sequences.SelectMany(sequence =>
            Named(sequence.Definition).Select(id => (id, new ApplicationUse(sequence.Id, sequence.Name, ApplicationUseKind.Sequence))));

        return byBundles.Concat(byApplications).Concat(bySequences)
            .GroupBy(use => use.Used)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ApplicationUse>)
                [
                    .. group.Select(use => use.By).Distinct().OrderBy(use => Order(use.Kind))
                        .ThenBy(use => use.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(use => use.Id),
                ]);
    }

    // The applications and bundles whose uses changed with a sequence save. A null definition means the sequence doesn't
    // exist yet, or no longer does. A rename changes every one it names.
    public static IReadOnlyList<Guid> Changed(string? before, string? beforeName, string? after, string? afterName)
    {
        HashSet<Guid> was = before is null ? [] : [.. Named(before)];
        HashSet<Guid> now = after is null ? [] : [.. Named(after)];

        return [.. was.Union(now).Where(id => !was.Contains(id) || !now.Contains(id) || beforeName != afterName)];
    }

    private static int Order(ApplicationUseKind kind) => kind switch
    {
        ApplicationUseKind.Bundle => 0,
        ApplicationUseKind.Application => 1,
        _ => 2,
    };

    // Each application and bundle the stored sequence names, once.
    private static IEnumerable<Guid> Named(string definition) =>
        ApplicationSites.Of(SequenceDocuments.Read(definition)).Select(site => site.Reference.Id).Distinct();
}
