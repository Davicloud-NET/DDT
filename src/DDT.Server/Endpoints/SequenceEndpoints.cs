// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Sequences;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

// A sequence with problems is saved with them, and never runs.
public static class SequenceEndpoints
{
    public static RouteGroupBuilder MapSequenceEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        RequestSizeLimitAttribute sizeLimit = new(SequenceLimits.MaxRequestBytes);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapGet("/{id:guid}", ReadAsync).RequireAuthorization(DdtPolicies.Viewer);

        // A sequence runs as SYSTEM on every machine it is given to, so only an administrator writes one.
        group.MapGet("/templates", ReadTemplates).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/validate", ValidateAsync).RequireAuthorization(DdtPolicies.Administrator).WithMetadata(sizeLimit);
        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Administrator).WithMetadata(sizeLimit);
        group.MapPut("/{id:guid}", SaveAsync).RequireAuthorization(DdtPolicies.Administrator).WithMetadata(sizeLimit);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    // SQLite cannot order by DateTimeOffset, and a library of sequences is small, so the order is made here.
    private static async Task<Ok<IReadOnlyList<SequenceSummary>>> ListAsync(
        DdtDbContext database,
        SequenceCatalog catalog,
        CancellationToken cancellationToken)
    {
        List<TaskSequence> sequences = await database.TaskSequences.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        SequenceReferences references = await catalog.ReferencesAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<SequenceSummary>>(
        [
            .. sequences
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Id)
                .Select(s => SequenceCatalog.Summary(s, references)),
        ]);
    }

    private static async Task<Results<Ok<SequenceView>, NotFound>> ReadAsync(
        Guid id,
        DdtDbContext database,
        SequenceCatalog catalog,
        CancellationToken cancellationToken)
    {
        TaskSequence? sequence = await database.TaskSequences
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return sequence is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(await catalog.ViewAsync(sequence, cancellationToken).ConfigureAwait(false));
    }

    private static Ok<IReadOnlyList<SequenceTemplate>> ReadTemplates(Guid? imageId, DdtSettings settings) =>
        TypedResults.Ok(SequenceTemplates.All(
            !string.IsNullOrWhiteSpace(settings.Current.Deployment.Domain.Name),
            !string.IsNullOrEmpty(settings.Current.Deployment.LocalAdministrator.Password),
            imageId ?? Guid.Empty));

    private static async Task<Results<Ok<SequenceValidation>, ProblemHttpResult>> ValidateAsync(
        HttpRequest request,
        SequenceCatalog catalog,
        CancellationToken cancellationToken)
    {
        (SequenceDefinition? definition, ProblemHttpResult? refusal) = await SequenceBodies
            .ReadAsync(request, DdtJsonContext.Default.SequenceDefinition, cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return refusal;
        }

        if (SequenceDocuments.Malformation(definition) is { } malformed)
        {
            return SequenceBodies.Malformed(malformed);
        }

        SequenceReferences references = await catalog.ReferencesAsync(cancellationToken).ConfigureAwait(false);

        // Checked as it would be stored, with the lowest version its kinds need.
        return TypedResults.Ok(SequenceChecks.Check(definition!.Normalised(), references));
    }

    private static async Task<Results<Created<SequenceView>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        HttpContext context,
        SequenceEditor editor,
        CancellationToken cancellationToken)
    {
        (CreateSequenceRequest? body, ProblemHttpResult? refusal) = await SequenceBodies
            .ReadAsync(context.Request, DdtJsonContext.Default.CreateSequenceRequest, cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return refusal;
        }

        if (SequenceDocuments.Malformation(body!.Definition) is { } malformed)
        {
            return SequenceBodies.Malformed(malformed);
        }

        EditOutcome<SequenceView> outcome = await editor.CreateAsync(body, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        return outcome.Saved is { } sequence
            ? TypedResults.Created($"/api/sequences/{sequence.Id:D}", sequence)
            : (outcome.Problems ?? new FieldProblems()).ToResult();
    }

    // The client decides what to do with a newer save: take it, or save its own edits over it knowingly.
    private static async Task<Results<Ok<SequenceView>, Conflict<SequenceView>, NotFound, ValidationProblem, ProblemHttpResult>> SaveAsync(
        Guid id,
        HttpContext context,
        SequenceEditor editor,
        CancellationToken cancellationToken)
    {
        (SaveSequenceRequest? body, ProblemHttpResult? refusal) = await SequenceBodies
            .ReadAsync(context.Request, DdtJsonContext.Default.SaveSequenceRequest, cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return refusal;
        }

        if (SequenceDocuments.Malformation(body!.Definition) is { } malformed)
        {
            return SequenceBodies.Malformed(malformed);
        }

        EditOutcome<SequenceView> outcome = await editor.SaveAsync(id, body, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            { Saved: { } sequence } => TypedResults.Ok(sequence),
            { Current: { } current } => TypedResults.Conflict(current),
            { Problems: { } problems } => problems.ToResult(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext context,
        SequenceEditor editor,
        CancellationToken cancellationToken)
    {
        (bool found, ServerMessage? refusal) = await editor.DeleteAsync(id, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        if (!found)
        {
            return TypedResults.NotFound();
        }

        return refusal is null ? TypedResults.NoContent() : ServerProblems.Problem(refusal, StatusCodes.Status409Conflict);
    }
}
