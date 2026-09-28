// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Messages;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

public static class DeploymentEndpoints
{
    public static RouteGroupBuilder MapDeploymentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // The runs of every machine, for the run history.
        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);

        // What the assign dialog needs: whether a domain wants a computer name, and what an assignment does to a waiting
        // machine. The settings stay on the server, since they hold the deployment passwords.
        group.MapGet("/options", ReadOptions).RequireAuthorization(DdtPolicies.Viewer);

        // Signs in to the domain with the join account's password, so only those who manage sequences may.
        group.MapPost("/domain-check", CheckDomainJoinAsync).RequireAuthorization(DdtPolicies.Administrator);

        // Viewers read the definition a run was given, scripts included, as they read the sequences.
        group.MapGet("/{id:guid}", ReadAsync).RequireAuthorization(DdtPolicies.Viewer);

        return group;
    }

    private static async Task<Results<Ok<RunHistoryPage>, ValidationProblem>> ListAsync(
        [AsParameters] RunHistoryQuery query,
        RunHistory history,
        CancellationToken cancellationToken) =>
        await history.PageAsync(query, cancellationToken).ConfigureAwait(false) is { } page
            ? TypedResults.Ok(page)
            : ServerProblems.Validation("before", ServerMessages.DeploymentHistoryCursor.With());

    // Three booleans and the time, from one snapshot, and nothing else of the settings: Viewers read this.
    private static Ok<DeploymentOptionsView> ReadOptions(DdtSettings settings, TimeProvider timeProvider)
    {
        SettingsSnapshot snapshot = settings.Current;

        return TypedResults.Ok(new DeploymentOptionsView(
            !string.IsNullOrWhiteSpace(snapshot.Deployment.Domain.Name),
            snapshot.Machines.RequireWebApproval,
            snapshot.Machines.ZeroTouchEnabled,
            timeProvider.GetUtcNow()));
    }

    private static async Task<Ok<DomainJoinCheckView>> CheckDomainJoinAsync(
        DomainJoinCheckRequest request,
        HttpContext context,
        DomainJoinCheck check,
        DdtDbContext database,
        CancellationToken cancellationToken)
    {
        DomainJoinCheckView result = await check.RunAsync(request.OrganizationalUnit, cancellationToken).ConfigureAwait(false);
        DomainJoinFinding last = result.Findings[^1];

        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.DomainJoinChecked,
            result.Domain,
            Actor.Of(context),
            result.CheckedUtc,
            $"Checked whether {result.UserName ?? "the join account"} can join {result.Domain ?? "a domain"}" +
                (result.Controller is null ? "" : $" at {result.Controller}") + $": {(result.CanJoin ? "it can" : "it cannot")}. {last.Text}"));

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<DeploymentView>, NotFound>> ReadAsync(
        Guid id,
        DdtDbContext database,
        CancellationToken cancellationToken) =>
        await DeploymentViews.ReadAsync(database, id, cancellationToken).ConfigureAwait(false) is { } view
            ? TypedResults.Ok(view)
            : TypedResults.NotFound();
}
