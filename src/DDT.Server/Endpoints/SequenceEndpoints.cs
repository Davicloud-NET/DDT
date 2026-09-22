// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Sequences;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DDT.Server.Endpoints;

// Saving checks only that the document can be stored. Every other problem is saved with it and returned, so an
// editor that saves as the administrator types keeps a draft; a sequence with problems never runs.
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

    private static Ok<IReadOnlyList<SequenceTemplate>> ReadTemplates(Guid? imageId, IOptions<DeploymentOptions> options) =>
        TypedResults.Ok(SequenceTemplates.All(
            !string.IsNullOrWhiteSpace(options.Value.Domain.Name),
            !string.IsNullOrEmpty(options.Value.LocalAdministrator.Password),
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

        return TypedResults.Ok(SequenceChecks.Check(definition!, references));
    }

    private static async Task<Results<Created<SequenceView>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        SequenceCatalog catalog,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        (CreateSequenceRequest? body, ProblemHttpResult? refusal) = await SequenceBodies
            .ReadAsync(request, DdtJsonContext.Default.CreateSequenceRequest, cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return refusal;
        }

        if (SequenceDocuments.Malformation(body!.Definition) is { } malformed)
        {
            return SequenceBodies.Malformed(malformed);
        }

        if (await FieldProblemsAsync(database, null, body.Name, body.Description, cancellationToken).ConfigureAwait(false) is { } problems)
        {
            return problems;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string name = body.Name.Trim();
        TaskSequence sequence = new()
        {
            Id = Guid.CreateVersion7(now),
            Name = name,
            NormalizedName = Normalize(name),
            Description = Description(body.Description),
            Definition = SequenceDocuments.Write(body.Definition),
            Revision = 1,
            CreatedUtc = now,
            UpdatedUtc = now,
            UpdatedByUserId = Principals.UserId(user),
            UpdatedByName = user.Identity?.Name,
        };

        database.TaskSequences.Add(sequence);
        database.AuditEvents.Add(Audit(
            AuditActions.SequenceCreated,
            sequence,
            user,
            context,
            now,
            $"{name}, {body.Definition.Steps.Count} steps."));

        if (await CommitAsync(database, sequence, cancellationToken).ConfigureAwait(false) is { } taken)
        {
            return taken;
        }

        live.SequenceChanged(new SequenceChangedEvent(sequence.Id, sequence.Revision, sequence.UpdatedByName));

        return TypedResults.Created($"/api/sequences/{sequence.Id:D}", await catalog.ViewAsync(sequence, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Results<Ok<SequenceView>, Conflict<SequenceView>, NotFound, ValidationProblem, ProblemHttpResult>> SaveAsync(
        Guid id,
        HttpRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        SequenceCatalog catalog,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        (SaveSequenceRequest? body, ProblemHttpResult? refusal) = await SequenceBodies
            .ReadAsync(request, DdtJsonContext.Default.SaveSequenceRequest, cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return refusal;
        }

        if (SequenceDocuments.Malformation(body!.Definition) is { } malformed)
        {
            return SequenceBodies.Malformed(malformed);
        }

        TaskSequence? sequence = await database.TaskSequences.FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);

        if (sequence is null)
        {
            return TypedResults.NotFound();
        }

        // The client decides what to do with a newer save: take it, or save its own edits over it knowingly.
        if (body.Revision != sequence.Revision)
        {
            return TypedResults.Conflict(await catalog.ViewAsync(sequence, cancellationToken).ConfigureAwait(false));
        }

        if (await FieldProblemsAsync(database, id, body.Name, body.Description, cancellationToken).ConfigureAwait(false) is { } problems)
        {
            return problems;
        }

        string name = body.Name.Trim();
        string? description = Description(body.Description);
        string definition = SequenceDocuments.Write(body.Definition);

        // An autosave of what is already stored changes nothing and records nothing.
        if (name == sequence.Name && description == sequence.Description && definition == sequence.Definition)
        {
            return TypedResults.Ok(await catalog.ViewAsync(sequence, cancellationToken).ConfigureAwait(false));
        }

        string changes = SequenceChanges.Describe(
            sequence.Name,
            sequence.Description,
            SequenceDocuments.Read(sequence.Definition),
            name,
            description,
            body.Definition);

        sequence.Name = name;
        sequence.NormalizedName = Normalize(name);
        sequence.Description = description;
        sequence.Definition = definition;
        sequence.Revision++;
        sequence.UpdatedUtc = timeProvider.GetUtcNow();
        sequence.UpdatedByUserId = Principals.UserId(user);
        sequence.UpdatedByName = user.Identity?.Name;

        database.AuditEvents.Add(Audit(
            AuditActions.SequenceChanged,
            sequence,
            user,
            context,
            sequence.UpdatedUtc,
            $"Revision {sequence.Revision}. {changes}"));

        try
        {
            if (await CommitAsync(database, sequence, cancellationToken).ConfigureAwait(false) is { } taken)
            {
                return taken;
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            TaskSequence? current = await database.TaskSequences
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return current is null
                ? TypedResults.NotFound()
                : TypedResults.Conflict(await catalog.ViewAsync(current, cancellationToken).ConfigureAwait(false));
        }

        live.SequenceChanged(new SequenceChangedEvent(sequence.Id, sequence.Revision, sequence.UpdatedByName));

        return TypedResults.Ok(await catalog.ViewAsync(sequence, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        TaskSequence? sequence = await database.TaskSequences.FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);

        if (sequence is null)
        {
            return TypedResults.NotFound();
        }

        int rules = await database.AssignmentRules.CountAsync(r => r.TaskSequenceId == id, cancellationToken).ConfigureAwait(false);

        if (rules > 0)
        {
            return TypedResults.Problem(
                title: rules == 1
                    ? "A rule chooses this sequence. Delete the rule or let it choose another sequence, then delete this one."
                    : $"{rules} rules choose this sequence. Delete them or let them choose another sequence, then delete this one.",
                statusCode: StatusCodes.Status409Conflict);
        }

        database.TaskSequences.Remove(sequence);
        database.AuditEvents.Add(Audit(
            AuditActions.SequenceDeleted,
            sequence,
            user,
            context,
            timeProvider.GetUtcNow(),
            $"{sequence.Name}, revision {sequence.Revision}."));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        // A save raises the revision, and a rule created meanwhile holds the sequence by its foreign key.
        catch (DbUpdateException)
        {
            return TypedResults.Problem(
                title: "The sequence changed, or a rule chose it, while it was being deleted. Look at it again before deleting it.",
                statusCode: StatusCodes.Status409Conflict);
        }

        live.SequenceChanged(new SequenceChangedEvent(sequence.Id, null, user.Identity?.Name));

        return TypedResults.NoContent();
    }

    private static async Task<ValidationProblem?> FieldProblemsAsync(
        DdtDbContext database,
        Guid? id,
        string? name,
        string? description,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> problems = [];
        string trimmed = name?.Trim() ?? "";

        if (trimmed.Length is 0 or > SequenceLimits.MaxNameLength || trimmed.Any(char.IsControl))
        {
            problems["name"] = [$"The name must have 1 to {SequenceLimits.MaxNameLength} characters and no control characters."];
        }
        else if (await NameTakenAsync(database, id, trimmed, cancellationToken).ConfigureAwait(false))
        {
            problems["name"] = [$"Another sequence is already called {trimmed}. Choose another name."];
        }

        if (Description(description)?.Length > SequenceLimits.MaxDescriptionLength)
        {
            problems["description"] = [$"The description can have at most {SequenceLimits.MaxDescriptionLength} characters."];
        }

        return problems.Count > 0 ? TypedResults.ValidationProblem(problems) : null;
    }

    private static Task<bool> NameTakenAsync(DdtDbContext database, Guid? id, string name, CancellationToken cancellationToken)
    {
        string normalized = Normalize(name);

        return database.TaskSequences.AnyAsync(s => s.NormalizedName == normalized && s.Id != id, cancellationToken);
    }

    // The unique index settles two saves that took the same name at once; the loser is told as if it had been first.
    private static async Task<ValidationProblem?> CommitAsync(DdtDbContext database, TaskSequence sequence, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return null;
        }
        catch (DbUpdateException exception) when (exception is not DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();

            if (!await NameTakenAsync(database, sequence.Id, sequence.Name, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }

            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = [$"Another sequence is already called {sequence.Name}. Choose another name."],
            });
        }
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();

    // PostgreSQL text cannot hold a NUL.
    private static string? Description(string? description)
    {
        string? text = description?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();

        return string.IsNullOrEmpty(text) ? null : text;
    }

    // The detail names steps, and a step name is whatever the editor sent, NUL included, which PostgreSQL refuses.
    private static AuditEvent Audit(
        string action,
        TaskSequence sequence,
        ClaimsPrincipal user,
        HttpContext context,
        DateTimeOffset now,
        string detail) => new()
        {
            OccurredUtc = now,
            Action = action,
            ActorUserId = Principals.UserId(user),
            ActorName = user.Identity?.Name,
            SubjectId = sequence.Id.ToString("D"),
            SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
            Detail = StoredText.Bound(detail, AuditEvent.MaxDetailLength),
        };
}
