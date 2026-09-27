// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Messages;
using DDT.Contracts.Rules;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Rules;
using DDT.Server.Sequences;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

// Rules are standing configuration that choose code to run as SYSTEM without anyone choosing it each time, so only
// an administrator writes them. The last save wins: rules are edited rarely.
public static class RuleEndpoints
{
    public static RouteGroupBuilder MapRuleEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<AssignmentRuleView>>> ListAsync(DdtDbContext database, CancellationToken cancellationToken) =>
        TypedResults.Ok<IReadOnlyList<AssignmentRuleView>>(await AssignmentRuleViews.ListAsync(database, cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Created<AssignmentRuleView>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        SaveAssignmentRuleRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        AssignmentRule rule = new() { MatchKey = "", CreatedUtc = now };

        Results<Ok<AssignmentRuleView>, ValidationProblem, ProblemHttpResult> saved = await SaveAsync(
            rule,
            request,
            AuditActions.RuleCreated,
            user,
            context,
            database,
            live,
            now,
            cancellationToken).ConfigureAwait(false);

        return saved.Result switch
        {
            Ok<AssignmentRuleView> ok => TypedResults.Created($"/api/rules/{rule.Id:D}", ok.Value),
            ValidationProblem problem => problem,
            ProblemHttpResult problem => problem,
            _ => throw new InvalidOperationException("The save ended without a result."),
        };
    }

    private static async Task<Results<Ok<AssignmentRuleView>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id,
        SaveAssignmentRuleRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        AssignmentRule? rule = await database.AssignmentRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);

        if (rule is null)
        {
            return TypedResults.NotFound();
        }

        Results<Ok<AssignmentRuleView>, ValidationProblem, ProblemHttpResult> saved = await SaveAsync(
            rule,
            request,
            AuditActions.RuleChanged,
            user,
            context,
            database,
            live,
            timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);

        return saved.Result switch
        {
            Ok<AssignmentRuleView> ok => ok,
            ValidationProblem problem => problem,
            ProblemHttpResult problem => problem,
            _ => throw new InvalidOperationException("The save ended without a result."),
        };
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        AssignmentRule? rule = await database.AssignmentRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);

        if (rule is null)
        {
            return TypedResults.NotFound();
        }

        database.AssignmentRules.Remove(rule);
        database.AuditEvents.Add(Audit(
            AuditActions.RuleDeleted,
            rule,
            user,
            context,
            timeProvider.GetUtcNow(),
            $"The rule for {AssignmentRuleKeys.Describe(rule)}."));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        live.RulesChanged(await AssignmentRuleViews.ListAsync(database, cancellationToken).ConfigureAwait(false));

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<AssignmentRuleView>, ValidationProblem, ProblemHttpResult>> SaveAsync(
        AssignmentRule rule,
        SaveAssignmentRuleRequest request,
        string action,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        FieldProblems problems = new();
        string? mac = null;
        string? manufacturer = null;
        string? model = null;
        string? description = request.Description?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();

        if (request.Kind == AssignmentRuleKind.Mac)
        {
            mac = RegistrationValidator.NormaliseMac(request.Mac?.Trim());

            if (mac is null)
            {
                problems.Add("mac", ServerMessages.MacEnterFull.With());
            }
        }
        else if (request.Kind == AssignmentRuleKind.Model)
        {
            if (HardwareModels.Problem(request.Manufacturer, required: false, wildcard: false) is { } manufacturerProblem)
            {
                problems.Add("manufacturer", manufacturerProblem);
            }

            if (HardwareModels.Problem(request.Model, required: true, wildcard: true) is { } modelProblem)
            {
                problems.Add("model", modelProblem);
            }

            manufacturer = HardwareModels.Clean(request.Manufacturer);
            model = HardwareModels.Clean(request.Model);
        }
        else
        {
            problems.Add("kind", ServerMessages.RuleChooseKind.With());
        }

        if (description?.Length > AssignmentRuleKeys.MaxDescriptionLength)
        {
            problems.Add("description", ServerMessages.DescriptionLength.With("max", AssignmentRuleKeys.MaxDescriptionLength));
        }

        TaskSequence? sequence = await database.TaskSequences
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SequenceId, cancellationToken)
            .ConfigureAwait(false);

        if (sequence is null)
        {
            problems.Add("sequenceId", ServerMessages.RuleSequenceGone.With());
        }

        if (problems.Count > 0)
        {
            return problems.ToResult();
        }

        string matchKey = AssignmentRuleKeys.MatchKey(request.Kind, mac, manufacturer, model);

        if (await ConflictAsync(database, rule.Id, matchKey, cancellationToken).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        if (rule.Id == Guid.Empty)
        {
            rule.Id = Guid.CreateVersion7(now);
            database.AssignmentRules.Add(rule);
        }

        rule.Kind = request.Kind;
        rule.MatchKey = matchKey;
        rule.Mac = mac;
        rule.Manufacturer = manufacturer;
        rule.Model = model;
        rule.TaskSequenceId = sequence!.Id;
        rule.Description = string.IsNullOrEmpty(description) ? null : description;
        rule.UpdatedUtc = now;
        rule.UpdatedByUserId = Principals.UserId(user);
        rule.UpdatedByName = user.Identity?.Name;

        database.AuditEvents.Add(Audit(
            action,
            rule,
            user,
            context,
            now,
            $"The rule for {AssignmentRuleKeys.Describe(rule)} chooses {sequence.Name}."));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception is not DbUpdateConcurrencyException)
        {
            // Another administrator saved the same rule a moment earlier; the unique index kept only theirs.
            database.ChangeTracker.Clear();

            if (await ConflictAsync(database, rule.Id, matchKey, cancellationToken).ConfigureAwait(false) is { } raced)
            {
                return raced;
            }

            throw;
        }

        live.RulesChanged(await AssignmentRuleViews.ListAsync(database, cancellationToken).ConfigureAwait(false));

        return TypedResults.Ok(AssignmentRuleViews.From(rule, sequence.Name));
    }

    private static async Task<ProblemHttpResult?> ConflictAsync(DdtDbContext database, Guid id, string matchKey, CancellationToken cancellationToken)
    {
        AssignmentRule? existing = await database.AssignmentRules
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.MatchKey == matchKey && r.Id != id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return null;
        }

        string sequence = (await AssignmentRuleViews.SequenceNamesAsync(database, cancellationToken).ConfigureAwait(false))[existing.TaskSequenceId];

        return ServerProblems.Problem(
            ServerMessages.RuleExists.With("rule", AssignmentRuleKeys.DescribeMessage(existing), "sequence", sequence),
            StatusCodes.Status409Conflict,
            new Dictionary<string, object?> { ["ruleId"] = existing.Id });
    }

    private static AuditEvent Audit(
        string action,
        AssignmentRule rule,
        ClaimsPrincipal user,
        HttpContext context,
        DateTimeOffset now,
        string detail) => new()
        {
            OccurredUtc = now,
            Action = action,
            ActorUserId = Principals.UserId(user),
            ActorName = user.Identity?.Name,
            SubjectId = rule.Id.ToString("D"),
            SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
            Detail = StoredText.Bound(detail, AuditEvent.MaxDetailLength),
        };
}
