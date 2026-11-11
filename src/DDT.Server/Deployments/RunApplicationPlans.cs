// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Server.Applications;
using DDT.Server.Packages;

namespace DDT.Server.Deployments;

// What a run freezes for its Install applications steps: each step's applications as its agent gets them, and the
// files it downloads. An artifact's source is the files' package, which keeps them in the library while the run is on.
public static class RunApplicationPlans
{
    // A plan for every step, an empty one too, so the start can add to it. The first step whose list fails is the problem.
    public static (IReadOnlyList<DeploymentApplicationPlan> Plans, ServerMessage? Problem) Fixed(
        Guid runId,
        SequenceDefinition definition,
        ApplicationLibrary library)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(library);

        List<DeploymentApplicationPlan> plans = [];

        foreach (InstallApplicationsStep step in SequenceTree.Nodes(definition).OfType<InstallApplicationsStep>())
        {
            ApplicationPlan plan = Resolve(step, library);

            if (plan.Problem is { } problem)
            {
                return ([], problem);
            }

            plans.Add(new DeploymentApplicationPlan
            {
                DeploymentId = runId,
                StepId = step.Id,
                Applications = Write([.. plan.Order.Select(application => Snapshot(application, library))]),
            });
        }

        return (plans, null);
    }

    // The files of the step's own list. A list that fails has none here; the run's creation refuses it.
    public static IEnumerable<DeploymentArtifact> FixedArtifacts(Guid runId, InstallApplicationsStep step, ApplicationLibrary library)
    {
        ArgumentNullException.ThrowIfNull(step);

        return Artifacts(runId, step.Id, Resolve(step, library).Order, library);
    }

    // One per package, however many of the applications share it.
    public static IEnumerable<DeploymentArtifact> Artifacts(Guid runId, Guid stepId, IEnumerable<Application> applications, ApplicationLibrary library)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(library);

        List<DeploymentArtifact> artifacts = [];
        HashSet<Guid> packages = [];

        foreach (Application application in applications)
        {
            if (application.PackageId is { } id && library.Files.TryGetValue(id, out Package? files) && packages.Add(id))
            {
                artifacts.Add(Artifact(runId, stepId, application, files));
            }
        }

        return artifacts;
    }

    public static AgentApplication Snapshot(Application application, ApplicationLibrary library)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(library);

        Package? files = application.PackageId is { } id ? library.Files.GetValueOrDefault(id) : null;

        return new AgentApplication(
            application.Id,
            application.Name,
            application.Kind,
            application.Command,
            ApplicationColumns.ReadCodes(application.SuccessExitCodes),
            ApplicationColumns.ReadCodes(application.RestartExitCodes),
            ApplicationColumns.ReadDetection(application.Detection),
            application.TimeoutMinutes,
            application.ContinueOnFailure,
            application.WingetId,
            application.WingetVersion,
            files is null ? null : new AgentApplicationFiles(files.Sha256, files.SizeBytes, files.ExpandedBytes, files.Name),
            ApplicationColumns.ReadIds(application.DependsOn));
    }

    public static IReadOnlyList<AgentApplication> Read(string? applications) =>
        string.IsNullOrEmpty(applications) ? [] : JsonSerializer.Deserialize(applications, DdtJsonContext.Default.IReadOnlyListAgentApplication) ?? [];

    public static string Write(IReadOnlyList<AgentApplication> applications) =>
        JsonSerializer.Serialize(applications, DdtJsonContext.Default.IReadOnlyListAgentApplication);

    private static ApplicationPlan Resolve(InstallApplicationsStep step, ApplicationLibrary library) =>
        ApplicationPlanner.Resolve(step.Name, step.Applications ?? [], library);

    private static DeploymentArtifact Artifact(Guid runId, Guid stepId, Application application, Package files) => new()
    {
        DeploymentId = runId,
        StepId = stepId,
        Kind = ArtifactKind.Application,
        SourceId = files.Id,
        Name = application.Name,
        Sha256 = files.Sha256,
        SizeBytes = files.SizeBytes,
        ExpandedBytes = files.ExpandedBytes,
    };
}
