// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Messages;
using DDT.Core.Applications;
using DDT.Server.Applications;

namespace DDT.Server.Deployments;

// The applications a step installs, in install order: a bundle stands for its members in order, and each application
// comes after its dependencies, once. The editors keep the library sound, but a run trusts nothing it did not check.
public static class ApplicationPlanner
{
    public static ApplicationPlan Resolve(string step, IEnumerable<ApplicationReference?> references, ApplicationLibrary library)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(library);

        List<Guid> roots = [];

        foreach (ApplicationReference reference in references.OfType<ApplicationReference>())
        {
            if (reference.Kind != ApplicationReferenceKind.Bundle)
            {
                roots.Add(reference.Id);
            }
            else if (library.Bundles.TryGetValue(reference.Id, out ApplicationBundle? bundle))
            {
                roots.AddRange(ApplicationColumns.ReadIds(bundle.Members));
            }
            else
            {
                return Failed(ServerMessages.SequenceStepApplicationGone.With("step", step));
            }
        }

        ApplicationGraphResult graph = ApplicationGraph.Order(
            roots,
            id => library.Applications.TryGetValue(id, out Application? application) ? ApplicationColumns.ReadIds(application.DependsOn) : null);

        if (graph.Problem is { } problem)
        {
            return Failed(Message(step, problem, library));
        }

        Application[] order = [.. graph.Order.Select(id => library.Applications[id])];

        return order.FirstOrDefault(application => application.PackageId is { } files && !library.Files.ContainsKey(files)) is { } lost
            ? Failed(ServerMessages.DeploymentApplicationFilesGone.With("application", lost.Name, "step", step))
            : new ApplicationPlan(order, null);
    }

    private static ApplicationPlan Failed(ServerMessage problem) => new([], problem);

    private static ServerMessage Message(string step, ApplicationGraphProblem problem, ApplicationLibrary library)
    {
        string NameOf(Guid id) => library.Applications.GetValueOrDefault(id)?.Name ?? id.ToString("D");

        if (problem.Kind == ApplicationGraphProblemKind.Cycle)
        {
            return ServerMessages.DeploymentApplicationCycle.With("step", step, "path", string.Join(" > ", problem.Path.Select(NameOf)));
        }

        return problem.NamedBy is { } namedBy
            ? ServerMessages.DeploymentApplicationDependencyGone.With("application", NameOf(namedBy), "step", step)
            : ServerMessages.SequenceStepApplicationGone.With("step", step);
    }
}
