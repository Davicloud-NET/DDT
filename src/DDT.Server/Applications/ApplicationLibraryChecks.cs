// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Applications;
using DDT.Contracts.Messages;
using DDT.Core.Applications;
using DDT.Server.Endpoints;
using DDT.Server.Images;
using DDT.Server.Packages;

namespace DDT.Server.Applications;

// The checks of a save that need the stored library: names unique across applications and bundles, files that exist
// and, for MSIX, hold packages DISM can take, dependencies and members that exist, and no dependency circle. A field the
// form rules refused is not checked again.
internal static class ApplicationLibraryChecks
{
    // self is the application's id, a new one's too.
    public static (ApplicationFields? Fields, FieldProblems? Problems) Check(
        SaveApplicationRequest request,
        Guid self,
        bool isNew,
        ApplicationRows rows,
        ImageStore store)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rows);

        IReadOnlyList<ApplicationProblem> refused = ApplicationRules.Check(request, isNew ? null : self);
        FieldProblems problems = Problems(refused);
        bool Refused(string field) => refused.Any(problem => problem.Field.StartsWith(field, StringComparison.Ordinal));

        if (!Refused("name"))
        {
            CheckName(request.Name, self, rows, problems);
        }

        if (!Refused("packageId") && request.PackageId is { } packageId)
        {
            // MSIX files an MSIX application has already were read when it was saved with them.
            bool read = !isNew && rows.Applications.GetValueOrDefault(self) is { Kind: ApplicationKind.Msix } stored && stored.PackageId == packageId;
            CheckFiles(packageId, request.Kind == ApplicationKind.Msix && !read, rows, store, problems);
        }

        if (!Refused("dependsOn"))
        {
            CheckDependencies(self, ApplicationNames.Clean(request.Name) ?? "", request.DependsOn ?? [], rows, problems);
        }

        return problems.Count > 0 ? (null, problems) : (ApplicationFields.From(request), null);
    }

    public static FieldProblems Check(SaveApplicationBundleRequest request, Guid self, ApplicationRows rows)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rows);

        IReadOnlyList<ApplicationProblem> refused = ApplicationRules.Check(request);
        FieldProblems problems = Problems(refused);

        if (!refused.Any(problem => problem.Field == "name"))
        {
            CheckName(request.Name, self, rows, problems);
        }

        if (!refused.Any(problem => problem.Field.StartsWith("members", StringComparison.Ordinal)))
        {
            IReadOnlyList<Guid> members = request.Members ?? [];

            for (int index = 0; index < members.Count; index++)
            {
                if (!rows.Applications.ContainsKey(members[index]))
                {
                    problems.Add(At("members", index), ServerMessages.ApplicationGone.With());
                }
            }
        }

        return problems;
    }

    private static FieldProblems Problems(IReadOnlyList<ApplicationProblem> refused)
    {
        FieldProblems problems = new();

        foreach (ApplicationProblem problem in refused)
        {
            problems.Add(problem.Field, problem.Message);
        }

        return problems;
    }

    private static void CheckFiles(Guid packageId, bool readMsix, ApplicationRows rows, ImageStore store, FieldProblems problems)
    {
        if (!rows.Files.TryGetValue(packageId, out Package? files))
        {
            problems.Add("packageId", ServerMessages.ApplicationFilesGone.With());
        }
        else if (readMsix && MsixFilesCheck.ProblemOf(store, files) is { } problem)
        {
            problems.Add("packageId", problem);
        }
    }

    // Applications and bundles share one namespace, because rules and answers name them in one list.
    private static void CheckName(string name, Guid self, ApplicationRows rows, FieldProblems problems)
    {
        string? normalized = ApplicationNames.Normalize(name);
        bool taken = rows.Applications.Values.Any(a => a.Id != self && a.NormalizedName == normalized)
            || rows.Bundles.Any(b => b.Id != self && b.NormalizedName == normalized);

        if (taken)
        {
            problems.Add("name", ServerMessages.ApplicationNameTaken.With("name", ApplicationNames.Clean(name) ?? name));
        }
    }

    // The graph is walked with the saved application's new dependencies in place of its stored ones.
    private static void CheckDependencies(Guid self, string name, IReadOnlyList<Guid> dependsOn, ApplicationRows rows, FieldProblems problems)
    {
        bool missing = false;

        for (int index = 0; index < dependsOn.Count; index++)
        {
            if (!rows.Applications.ContainsKey(dependsOn[index]))
            {
                problems.Add(At("dependsOn", index), ServerMessages.ApplicationGone.With());
                missing = true;
            }
        }

        if (missing)
        {
            return;
        }

        ApplicationGraphResult graph = ApplicationGraph.Order(
            [self],
            id => id == self
                ? dependsOn
                : rows.Applications.TryGetValue(id, out Application? application) ? ApplicationColumns.ReadIds(application.DependsOn) : null);
        string NameOf(Guid id) => id == self ? name : rows.Applications.GetValueOrDefault(id)?.Name ?? id.ToString("D");

        switch (graph.Problem)
        {
            case { Kind: ApplicationGraphProblemKind.Cycle, Path: var path }:
                problems.Add("dependsOn", ServerMessages.ApplicationDependencyCycle.With("path", string.Join(" → ", path.Select(NameOf))));
                break;
            case { Kind: ApplicationGraphProblemKind.Missing, NamedBy: { } namedBy }:
                problems.Add("dependsOn", ServerMessages.ApplicationDependencyGone.With("application", NameOf(namedBy)));
                break;
        }
    }

    private static string At(string field, int index) => string.Create(CultureInfo.InvariantCulture, $"{field}[{index}]");
}
