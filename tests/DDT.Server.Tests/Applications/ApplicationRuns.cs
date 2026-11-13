// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.ApplicationRequests;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests.Applications;

// Runs that install applications, and what the server froze for them.
internal static class ApplicationRuns
{
    public const string StepName = "Install the tools";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static InstallApplicationsStep Install(string? variable, params ApplicationReference[] applications) =>
        new() { Id = Guid.NewGuid(), Name = StepName, Applications = applications, Variable = variable };

    public static ApplicationReference Of(ApplicationView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new ApplicationReference(view.Id, ApplicationReferenceKind.Application);
    }

    public static ApplicationReference Of(ApplicationBundleView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new ApplicationReference(view.Id, ApplicationReferenceKind.Bundle);
    }

    public static Guid PackageOf(ApplicationView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return Assert.IsType<ApplicationFiles>(view.Files).PackageId;
    }

    // An application with an uploaded installer of its own.
    public static async Task<ApplicationView> WithFilesAsync(this SignedInClient administrator, string name, params Guid[] dependsOn)
    {
        ArgumentNullException.ThrowIfNull(administrator);

        ApplicationUpload upload = await administrator.UploadedApplicationAsync(RandomNumberGenerator.GetBytes(512), $"{Guid.NewGuid():N}.exe");

        return await administrator.CreatedApplicationAsync(Command(Unique(name), upload.Files.PackageId, dependsOn));
    }

    // Partition and apply, then the steps.
    public static async Task<SequenceView> SequenceAsync(
        this DdtApplication application,
        IReadOnlyList<InputDeclaration>? inputs,
        params SequenceStep[] steps)
    {
        ArgumentNullException.ThrowIfNull(application);

        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceView sequence = await (await application.AdministratorAsync()).CreatedSequenceAsync(
            SequenceRequests.Definition([.. SequenceRequests.Minimal(image.Id).Steps, .. steps]) with { Inputs = inputs });

        Assert.Empty(sequence.Problems);

        return sequence;
    }

    // An approved machine with the sequence assigned, and the run its agent was handed.
    public static async Task<(DeployingMachine Machine, AgentRun Run)> AssignedAsync(
        this DdtApplication application,
        SequenceView sequence,
        IReadOnlyList<InputAnswer>? answers = null,
        Func<DeployingMachine, Task>? before = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(sequence);

        SignedInClient administrator = await application.AdministratorAsync();
        DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);

        if (before is not null)
        {
            await before(machine);
        }

        await administrator.AssignedAsync(machine.Id, sequence.Id, "APPS-PC", answers);

        return (machine, (await machine.NextAsync()).Run!);
    }

    // Every step before the one at running is done, and that one runs.
    public static AgentRunReport Reached(AgentRun run, int running, SequencePhase phase = SequencePhase.WindowsPE)
    {
        ArgumentNullException.ThrowIfNull(run);

        return Report(
            DeploymentState.Running,
            [.. run.Sequence.Steps.Take(running + 1).Select((step, index) => Step(step, index < running ? StepState.Done : StepState.Running))]) with
        {
            Phase = phase,
        };
    }

    // The service in Windows, registered with the run token the agent in Windows PE handed over.
    public static async Task<string> ServiceTokenAsync(DeployingMachine machine, AgentEnvironment environment = AgentEnvironment.Windows)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return (await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { RunToken = machine.RunToken, Environment = environment }))).Token!;
    }

    // The step's plan as stored, or null if it has none.
    public static async Task<IReadOnlyList<AgentApplication>?> PlanAsync(this DdtApplication application, Guid runId, Guid stepId)
    {
        ArgumentNullException.ThrowIfNull(application);

        string? stored = await application.QueryAsync(database => database.DeploymentApplicationPlans
            .AsNoTracking()
            .Where(p => p.DeploymentId == runId && p.StepId == stepId)
            .Select(p => p.Applications)
            .FirstOrDefaultAsync(Cancellation));

        return stored is null ? null : RunApplicationPlans.Read(stored);
    }

    public static Task<List<DeploymentArtifact>> ApplicationArtifactsAsync(this DdtApplication application, Guid runId)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.QueryAsync(database => database.DeploymentArtifacts
            .AsNoTracking()
            .Where(a => a.DeploymentId == runId && a.Kind == ArtifactKind.Application)
            .OrderBy(a => a.Id)
            .ToListAsync(Cancellation));
    }

    public static Task<Package> FilesOfAsync(this DdtApplication application, ApplicationView view)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(view);

        Guid packageId = PackageOf(view);

        return application.QueryAsync(database => database.Packages.AsNoTracking().SingleAsync(p => p.Id == packageId, Cancellation));
    }
}
