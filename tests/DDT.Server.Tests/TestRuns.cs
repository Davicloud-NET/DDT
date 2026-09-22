// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Packages;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// Runs and packages stored directly, for tests of what a run keeps in the library and of states only an agent's
// reports produce.
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
            Title = "A run",
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

    // Moves the machine's active run as the agent's reports would: Running makes the machine Deploying, and a run that
    // ends leaves it Done or Failed.
    public static async Task MoveRunAsync(this DdtApplication application, Guid machineId, DeploymentState state, string? error = null)
    {
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        Machine machine = await database.Machines.SingleAsync(m => m.Id == machineId, TestContext.Current.CancellationToken);
        Deployment run = await database.Deployments.SingleAsync(d => d.Id == machine.ActiveDeploymentId, TestContext.Current.CancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        run.State = state;
        run.UpdatedUtc = now;
        run.StartedUtc ??= now;
        run.Error = error;

        if (state == DeploymentState.Running)
        {
            machine.State = MachineState.Deploying;
        }
        else
        {
            run.FinishedUtc = now;
            machine.ActiveDeploymentId = null;
            machine.State = state == DeploymentState.Done ? MachineState.Done : MachineState.Failed;
        }

        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<Package> SeedPackageAsync(this DdtApplication application, PackageKind kind, params HardwareModel[] targets)
    {
        byte[] content = RandomNumberGenerator.GetBytes(512);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        ImageStore store = application.Services.GetRequiredService<ImageStore>();
        Directory.CreateDirectory(store.ObjectsDirectory);
        await File.WriteAllBytesAsync(store.ObjectPath(sha256), content, TestContext.Current.CancellationToken);

        Package package = new()
        {
            Id = Guid.CreateVersion7(),
            Name = $"Package {sha256[..8]}",
            Kind = kind,
            Sha256 = sha256,
            SizeBytes = content.Length,
            ExpandedBytes = content.Length * 3L,
            FileCount = 2,
            Targets = PackageTargets.Write(targets),
            UploadedUtc = DateTimeOffset.UtcNow,
        };

        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        database.Packages.Add(package);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        return package;
    }
}
