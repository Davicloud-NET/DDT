// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using DDT.Server.Sequences;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DeploymentStep = DDT.Server.Deployments.DeploymentStep;

namespace DDT.Server.Endpoints;

public static class DeploymentEndpoints
{
    public static RouteGroupBuilder MapDeploymentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // The assign dialog asks for a computer name only when the machine joins a domain, and says what the
        // assignment does to a waiting machine. The settings themselves stay on the server: they hold the
        // deployment passwords.
        group.MapGet("/options", ReadOptions).RequireAuthorization(DdtPolicies.Viewer);

        // Viewers read the definition a run was given, scripts included, as they read the sequences.
        group.MapGet("/{id:guid}", ReadAsync).RequireAuthorization(DdtPolicies.Viewer);

        return group;
    }

    private static Ok<DeploymentOptionsView> ReadOptions(
        DeploymentService deployments,
        IOptions<MachineOptions> machineOptions,
        TimeProvider timeProvider) =>
        TypedResults.Ok(new DeploymentOptionsView(
            deployments.DomainConfigured,
            machineOptions.Value.RequireWebApproval,
            deployments.ZeroTouchEnabled,
            timeProvider.GetUtcNow()));

    private static async Task<Results<Ok<DeploymentView>, NotFound>> ReadAsync(
        Guid id,
        DdtDbContext database,
        CancellationToken cancellationToken)
    {
        Deployment? run = await database.Deployments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return TypedResults.NotFound();
        }

        DeploymentSnapshot? snapshot = await database.DeploymentSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.DeploymentId == id, cancellationToken)
            .ConfigureAwait(false);

        List<DeploymentStep> steps = await database.DeploymentSteps
            .AsNoTracking()
            .Where(s => s.DeploymentId == id)
            .OrderBy(s => s.Index)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<DeploymentArtifact> artifacts = await database.DeploymentArtifacts
            .AsNoTracking()
            .Where(a => a.DeploymentId == id)
            .OrderBy(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new DeploymentView(
            DeploymentSummaries.From(run),
            run.MachineId,
            run.SequenceRevision,
            run.RuleId,
            snapshot is null ? null : SequenceDocuments.Read(snapshot.Definition),
            [.. steps.Select(DeploymentSummaries.Step)],
            [.. artifacts.Select(DeploymentSummaries.Artifact)]));
    }
}
