// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Server.Applications;

namespace DDT.Server.Sequences;

// Checks the applications and bundles a sequence names against the library. A missing one keeps the sequence a draft.
// winget runs in DDT's session in Windows, which only an answer file of DDT's sets up, so without one it's a warning.
public static class SequenceApplicationChecks
{
    public static void Check(SequenceDefinition definition, SequenceReferences references, List<SequenceProblem> problems, List<SequenceProblem> warnings)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(problems);
        ArgumentNullException.ThrowIfNull(warnings);

        IReadOnlyList<ApplicationSite> sites = ApplicationSites.Of(definition);

        foreach (ApplicationSite site in sites.Where(site => !Exists(site.Reference, references)))
        {
            problems.Add(SequenceProblem.From(
                StepId(site),
                site.Field,
                site.Step is { } step
                    ? ServerMessages.SequenceStepApplicationGone.With("step", step.Name ?? "")
                    : ServerMessages.SequenceInputApplicationGone.With("input", site.Input?.Name ?? "")));
        }

        if (SequenceTree.Nodes(definition).Any(node => node is WriteUnattendStep))
        {
            return;
        }

        // One warning per step or input, on its list as a whole.
        foreach (IGrouping<(Guid?, string), ApplicationSite> list in sites.GroupBy(site => (site.Step?.Id, List(site.Field))))
        {
            string[] winget =
            [
                .. Reached(list.Select(site => site.Reference), references)
                    .Where(application => application.Kind == ApplicationKind.Winget)
                    .Select(application => application.Name),
            ];

            if (winget.Length > 0)
            {
                warnings.Add(SequenceProblem.From(
                    StepId(list.First()),
                    list.Key.Item2,
                    ServerMessages.SequenceWingetNeedsSession.With("application", string.Join(", ", winget))));
            }
        }
    }

    private static bool Exists(ApplicationReference reference, SequenceReferences references) => reference.Kind switch
    {
        ApplicationReferenceKind.Application => references.Applications.ContainsKey(reference.Id),
        ApplicationReferenceKind.Bundle => references.ApplicationBundles.ContainsKey(reference.Id),
        _ => false,
    };

    // The applications the references install: themselves, a bundle's members and every dependency, each once.
    private static IEnumerable<Application> Reached(IEnumerable<ApplicationReference> named, SequenceReferences references)
    {
        Stack<Guid> open = new(named.SelectMany(reference => reference.Kind == ApplicationReferenceKind.Bundle
            ? references.ApplicationBundles.TryGetValue(reference.Id, out ApplicationBundle? bundle) ? ApplicationColumns.ReadIds(bundle.Members) : []
            : [reference.Id]).Reverse());
        HashSet<Guid> seen = [];

        while (open.TryPop(out Guid id))
        {
            if (!seen.Add(id) || !references.Applications.TryGetValue(id, out Application? application))
            {
                continue;
            }

            yield return application;

            foreach (Guid dependency in ApplicationColumns.ReadIds(application.DependsOn).Reverse())
            {
                open.Push(dependency);
            }
        }
    }

    private static Guid? StepId(ApplicationSite site) => site.Step is { } step && step.Id != Guid.Empty ? step.Id : null;

    // applications[2] is in the list applications, and inputs[0].offered[1] in inputs[0].offered.
    private static string List(string field) => field[..field.LastIndexOf('[')];
}
