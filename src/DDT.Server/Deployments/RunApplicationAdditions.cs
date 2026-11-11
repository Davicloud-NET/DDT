// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using DDT.Contracts.Deployments;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Values;
using DDT.Server.Applications;
using DDT.Server.Data;
using DDT.Server.Images;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// Appends what a step's value names to its plan when the run starts, and checks that every application file the run
// downloads is stored. Call with ImageStore.LibraryLock held, so nothing it adds goes before the caller saves.
public sealed class RunApplicationAdditions(DdtDbContext database, ImageStore store)
{
    private const string NotStarted = "The run did not start.";

    // The caller holds it from before AddAsync until it has saved.
    public Task<SemaphoreHold> EnterLibraryAsync(CancellationToken cancellationToken) => store.LibraryLock.EnterAsync(cancellationToken);

    // An English sentence for the run's error, or null. Nothing is added unless everything resolves.
    public async Task<string?> AddAsync(Deployment run, SequenceDefinition definition, ValueResolution resolution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(resolution);

        InstallApplicationsStep[] steps = [.. SequenceTree.Nodes(definition).OfType<InstallApplicationsStep>()];

        if (steps.Length == 0)
        {
            return null;
        }

        List<DeploymentArtifact> frozen = await database.DeploymentArtifacts
            .Where(a => a.DeploymentId == run.Id && a.Kind == ArtifactKind.Application)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        ApplicationLibrary? library = null;
        List<(DeploymentApplicationPlan Plan, IReadOnlyList<AgentApplication> Applications)> plans = [];
        List<DeploymentArtifact> added = [];

        foreach (InstallApplicationsStep step in steps)
        {
            if (Named(step, resolution) is not { } named)
            {
                continue;
            }

            library ??= await ApplicationLibrary.LoadAsync(database, cancellationToken).ConfigureAwait(false);
            ApplicationPlan resolved = Resolve(step, named.Value, library);

            if (resolved.Problem is { } problem)
            {
                return $"{NotStarted} {problem.Text}{Source(named.Name, resolution)}";
            }

            DeploymentApplicationPlan plan = await PlanAsync(run.Id, step.Id, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<AgentApplication> planned = RunApplicationPlans.Read(plan.Applications);
            HashSet<Guid> known = [.. planned.Select(application => application.Id)];
            Application[] appended = [.. resolved.Order.Where(application => !known.Contains(application.Id))];

            plans.Add((plan, [.. planned, .. appended.Select(application => RunApplicationPlans.Snapshot(application, library))]));
            added.AddRange(RunApplicationPlans.Artifacts(run.Id, step.Id, appended, library)
                .Where(artifact => !frozen.Any(a => a.StepId == artifact.StepId && a.SourceId == artifact.SourceId)));
        }

        if (frozen.Concat(added).FirstOrDefault(artifact => !File.Exists(store.ObjectPath(artifact.Sha256))) is { } lost)
        {
            string step = steps.FirstOrDefault(s => s.Id == lost.StepId)?.Name ?? "";

            return $"{NotStarted} {ServerMessages.DeploymentApplicationFilesGone.With("application", lost.Name, "step", step).Text}";
        }

        foreach ((DeploymentApplicationPlan plan, IReadOnlyList<AgentApplication> applications) in plans)
        {
            plan.Applications = RunApplicationPlans.Write(applications);
        }

        database.DeploymentArtifacts.AddRange(added);

        return null;
    }

    // The variable the step reads and its value, or null when the run has no value for it.
    private static (string Name, string Value)? Named(InstallApplicationsStep step, ValueResolution resolution)
    {
        string name = step.Variable?.Trim() ?? "";

        return name.Length > 0 && resolution.Effective.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? (name, value)
            : null;
    }

    private static ApplicationPlan Resolve(InstallApplicationsStep step, string value, ApplicationLibrary library)
    {
        (IReadOnlyList<ApplicationReference> references, string? unknown) = ApplicationTokens.Read(value, library);

        return unknown is null
            ? ApplicationPlanner.Resolve(step.Name, references, library)
            : new ApplicationPlan([], ServerMessages.DeploymentApplicationUnknown.With("step", step.Name, "name", unknown));
    }

    private async Task<DeploymentApplicationPlan> PlanAsync(Guid runId, Guid stepId, CancellationToken cancellationToken)
    {
        DeploymentApplicationPlan? plan = await database.DeploymentApplicationPlans
            .FindAsync([runId, stepId], cancellationToken)
            .ConfigureAwait(false);

        if (plan is null)
        {
            plan = new DeploymentApplicationPlan { DeploymentId = runId, StepId = stepId, Applications = RunApplicationPlans.Write([]) };
            database.DeploymentApplicationPlans.Add(plan);
        }

        return plan;
    }

    // Where the value the run uses came from, as a sentence after the problem.
    private static string Source(string name, ValueResolution resolution) =>
        resolution.Values.FirstOrDefault(value => !value.Overridden && string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase)) switch
        {
            { Source: ValueSource.Input } => $" {name} came from the answer to its input.",
            { Source: ValueSource.Machine } => $" {name} came from the machine's own values.",
            { Source: ValueSource.Rule, SourceName: var rule } => $" {name} came from the rule {rule}.",
            { Source: ValueSource.Role, SourceName: var role } => $" {name} came from the machine role {role}.",
            { Source: ValueSource.SequenceDefault } => $" {name} came from the sequence's default.",
            { Source: ValueSource.DeploymentDefault } => $" {name} came from the deployment defaults.",
            _ => "",
        };
}
