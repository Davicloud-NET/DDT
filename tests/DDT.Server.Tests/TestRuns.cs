// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Server.Data;
using DDT.Server.Deployments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// Runs stored directly, for tests of what a run's artifacts keep in the library.
internal static class TestRuns
{
    public static async Task<Guid> AddAssignedRunAsync(this DdtApplication application, ArtifactKind kind, Guid sourceId, string sha256)
    {
        using RegisteredMachine machine = await application.RegisterMachineAsync();
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Deployment run = new()
        {
            Id = Guid.CreateVersion7(),
            MachineId = machine.Id,
            ImageName = "A run",
            Sha256 = new string('0', 64),
            State = DeploymentState.Assigned,
            Source = DeploymentSource.Web,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        database.Deployments.Add(run);
        database.DeploymentArtifacts.Add(new DeploymentArtifact
        {
            DeploymentId = run.Id,
            StepId = Guid.NewGuid(),
            Kind = kind,
            SourceId = sourceId,
            Name = "Artifact",
            Sha256 = sha256,
            SizeBytes = 1,
        });

        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        return run.Id;
    }

    public static async Task EndRunAsync(this DdtApplication application, Guid runId)
    {
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

        Deployment run = await database.Deployments.SingleAsync(d => d.Id == runId, TestContext.Current.CancellationToken);
        run.State = DeploymentState.Done;
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
