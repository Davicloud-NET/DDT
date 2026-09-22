// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Rules;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Rules;
using DDT.Server.Sequences;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DeploymentStep = DDT.Server.Deployments.DeploymentStep;

namespace DDT.Server.Endpoints;

public static class MachineEndpoints
{
    public static RouteGroupBuilder MapMachineEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapGet("/models", ListModelsAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapGet("/{id:guid}/log", ReadLogAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapGet("/{id:guid}/sequence", ResolveSequenceAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPost("/{id:guid}/approve", ApproveAsync).RequireAuthorization(DdtPolicies.Operator);
        group.MapPost("/{id:guid}/reject", RejectAsync).RequireAuthorization(DdtPolicies.Operator);
        group.MapGet("/{id:guid}/deployments", ListDeploymentsAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPost("/{id:guid}/deployments", AssignAsync).RequireAuthorization(DdtPolicies.Operator);
        group.MapDelete("/{id:guid}/deployments/current", EndCurrentAsync).RequireAuthorization(DdtPolicies.Operator);

        // Anyone who reaches the server can register machines, so an operator can throw away the ones nobody
        // vouched for, one at a time or everything waiting from one address.
        group.MapDelete("/{id:guid}", RemoveAsync).RequireAuthorization(DdtPolicies.Operator);
        group.MapDelete("/", RemoveWaitingFromAsync).RequireAuthorization(DdtPolicies.Operator);

        return group;
    }

    // SQLite cannot order by DateTimeOffset, and a fleet this size sorts in memory for nothing. The order
    // must not depend on anything a poll changes, or rows move under an operator's pointer.
    private static async Task<Ok<IReadOnlyList<MachineSummary>>> ListAsync(
        DdtDbContext database,
        DeploymentService deployments,
        CancellationToken cancellationToken)
    {
        List<Machine> machines = await database.Machines.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Guid, Deployment> shown = await deployments.ShownForAsync(machines, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<MachineSummary>>(
        [
            .. machines
                .OrderBy(m => m.State == MachineState.Pending ? 0 : 1)
                .ThenByDescending(m => m.FirstSeenUtc)
                .ThenBy(m => m.Id)
                .Select(m => MachineSummaries.From(m, shown.GetValueOrDefault(m.Id))),
        ]);
    }

    // The models the registered machines report, for choosing a rule's or a package's model from what exists.
    // Placeholders are left out, because neither can match them.
    private static async Task<Ok<IReadOnlyList<HardwareModelCount>>> ListModelsAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        var reported = await database.Machines
            .AsNoTracking()
            .Select(m => new { m.Manufacturer, m.Model })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<HardwareModelCount>>(
        [
            .. reported
                .Select(m => (
                    Manufacturer: HardwareModels.IsPlaceholder(m.Manufacturer) ? null : HardwareModels.Clean(m.Manufacturer),
                    Model: HardwareModels.Clean(m.Model)))
                .Where(m => m.Model is not null && !HardwareModels.IsPlaceholder(m.Model))
                .GroupBy(m => (HardwareModels.Normalize(m.Manufacturer), HardwareModels.Normalize(m.Model)))
                .Select(group => (
                    Spelling: group.OrderBy(m => m.Manufacturer, StringComparer.Ordinal).ThenBy(m => m.Model, StringComparer.Ordinal).First(),
                    Count: group.Count()))
                .Select(model => new HardwareModelCount(model.Spelling.Manufacturer, model.Spelling.Model!, model.Count))
                .OrderByDescending(m => m.Machines)
                .ThenBy(m => m.Manufacturer, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.Model, StringComparer.OrdinalIgnoreCase),
        ]);
    }

    private static async Task<Results<Ok<MachineSequenceResolution>, NotFound>> ResolveSequenceAsync(
        Guid id,
        DdtDbContext database,
        SequenceResolver resolver,
        SequenceCatalog catalog,
        CancellationToken cancellationToken)
    {
        Machine? machine = await database.Machines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return TypedResults.NotFound();
        }

        SequenceResolution resolution = await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false);
        int problems = resolution.Sequence is { } sequence
            ? (await catalog.ViewAsync(sequence, cancellationToken).ConfigureAwait(false)).Problems.Count
            : 0;
        string explanation = problems switch
        {
            0 => resolution.Explanation,
            1 => $"{resolution.Explanation} {resolution.Sequence!.Name} has 1 problem, so it cannot run until it is fixed.",
            _ => $"{resolution.Explanation} {resolution.Sequence!.Name} has {problems} problems, so it cannot run until they are fixed.",
        };

        return TypedResults.Ok(new MachineSequenceResolution(
            resolution.Source,
            resolution.Sequence?.Id,
            resolution.Sequence?.Name,
            resolution.Rule?.Id,
            problems,
            explanation));
    }

    private static async Task<Results<Ok<IReadOnlyList<MachineLogEntry>>, NotFound>> ReadLogAsync(
        Guid id,
        long? after,
        DdtDbContext database,
        CancellationToken cancellationToken)
    {
        if (!await database.Machines.AnyAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.NotFound();
        }

        long from = after ?? 0;

        List<MachineLogEntry> entries = await database.MachineLogLines
            .AsNoTracking()
            .Where(line => line.MachineId == id && line.Id > from)
            .OrderBy(line => line.Id)
            .Take(MachineLogLimits.MaxLinesPerRead)
            .Select(line => new MachineLogEntry(line.Id, line.TimestampUtc, line.ReceivedUtc, line.Level, line.Message))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<MachineLogEntry>>(entries);
    }

    // With the sequence the page showed a rule choosing, the approval also runs it. Without one it runs nothing.
    private static async Task<Results<Ok<MachineSummary>, NotFound, ProblemHttpResult>> ApproveAsync(
        Guid id,
        ApproveMachineRequest? request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        DeploymentService deployments,
        ImageStore store,
        LiveNotifier live,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        IOptions<MachineOptions> options,
        CancellationToken cancellationToken)
    {
        string? Refusal(Machine machine) => machine.State != MachineState.Pending
            ? $"The machine is {machine.State}."
            : options.Value.RequireWebApproval && machine.SignedInByUserId is null
                ? "Nobody has signed in at this machine yet."
                : null;

        if (request?.ExpectedSequenceId is not { } expected)
        {
            return await TransitionAsync(
                id,
                user,
                context,
                database,
                deployments,
                live,
                timeProvider,
                loggerFactory,
                AuditActions.MachineApproved,
                Refusal,
                (machine, _, now) =>
                {
                    Approve(machine, Principals.UserId(user), now);

                    return Task.CompletedTask;
                },
                cancellationToken).ConfigureAwait(false);
        }

        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return TypedResults.NotFound();
        }

        if (Refusal(machine) is { } reason)
        {
            return TypedResults.Problem(title: reason, statusCode: StatusCodes.Status409Conflict);
        }

        Deployment run;

        // Under the library lock, so nothing the run downloads can be deleted between its lookup and the save.
        await store.LibraryLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            string? address = context.Connection.RemoteIpAddress?.ToString();
            DeploymentDecision decision = await deployments
                .AssignByRuleAsync(machine, expected, Principals.UserId(user), user.Identity?.Name, address, cancellationToken)
                .ConfigureAwait(false);

            if (decision.Outcome != DeploymentOutcome.Accepted)
            {
                return TypedResults.Problem(title: decision.Reason, statusCode: StatusCodes.Status409Conflict);
            }

            run = decision.Deployment!;
            DateTimeOffset now = timeProvider.GetUtcNow();
            Approve(machine, Principals.UserId(user), now);
            database.AuditEvents.Add(new AuditEvent
            {
                OccurredUtc = now,
                Action = AuditActions.MachineApproved,
                ActorUserId = Principals.UserId(user),
                ActorName = user.Identity?.Name,
                SubjectId = machine.Id.ToString("D"),
                SourceAddress = address,
                Detail = StoredText.Bound($"Was Pending. Approved to run {run.Title}, which a rule chose.", AuditEvent.MaxDetailLength),
            });

            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return TypedResults.Problem(
                    title: "The machine changed while this decision was made. Look at it again before deciding.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        }
        finally
        {
            store.LibraryLock.Release();
        }

        DeploymentLog.Changed(loggerFactory.CreateLogger(typeof(MachineEndpoints)), run, null);
        live.MachineChanged(machine, run);

        return TypedResults.Ok(MachineSummaries.From(machine, run));
    }

    private static void Approve(Machine machine, Guid? userId, DateTimeOffset now)
    {
        machine.State = MachineState.Approved;
        machine.ApprovedByUserId = userId;
        machine.ApprovedUtc = now;
        machine.FirstApprovedUtc ??= now;
    }

    // Newest first. SQLite cannot order by DateTimeOffset, and one machine has few runs, so they are ordered here.
    private static async Task<Results<Ok<IReadOnlyList<DeploymentSummary>>, NotFound>> ListDeploymentsAsync(
        Guid id,
        DdtDbContext database,
        CancellationToken cancellationToken)
    {
        if (!await database.Machines.AnyAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.NotFound();
        }

        List<Deployment> runs = await database.Deployments
            .AsNoTracking()
            .Where(d => d.MachineId == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<DeploymentSummary>>(
        [
            .. runs.OrderByDescending(d => d.CreatedUtc).ThenByDescending(d => d.Id).Select(DeploymentSummaries.From),
        ]);
    }

    private static Task<Results<Ok<MachineSummary>, NotFound, ProblemHttpResult>> RejectAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        DeploymentService deployments,
        LiveNotifier live,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            id,
            user,
            context,
            database,
            deployments,
            live,
            timeProvider,
            loggerFactory,
            AuditActions.MachineRejected,
            machine => machine.State is MachineState.Pending or MachineState.Approved or MachineState.Deploying or MachineState.Failed
                ? null
                : $"The machine is {machine.State}.",
            (machine, active, _) =>
            {
                // The generation bump kills every token already issued, so a rejected machine stops
                // mid request rather than at its next token refresh, a running deployment included.
                machine.State = MachineState.Rejected;
                machine.TokenGeneration++;
                machine.ApprovedByUserId = null;
                machine.ApprovedUtc = null;

                return deployments.EndForRejectionAsync(
                    machine,
                    active,
                    Principals.UserId(user),
                    user.Identity?.Name,
                    context.Connection.RemoteIpAddress?.ToString(),
                    cancellationToken);
            },
            cancellationToken);

    private static async Task<Results<Ok<MachineSummary>, NotFound, ProblemHttpResult, ValidationProblem>> AssignAsync(
        Guid id,
        AssignSequenceRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        DeploymentService deployments,
        ImageStore store,
        LiveNotifier live,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return TypedResults.NotFound();
        }

        // Under the library lock, so nothing the run downloads can be deleted between its lookup and the save.
        await store.LibraryLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            DeploymentDecision decision = await deployments
                .AssignAsync(machine, request, Principals.UserId(user), user.Identity?.Name, context.Connection.RemoteIpAddress?.ToString(), cancellationToken)
                .ConfigureAwait(false);

            return await CompleteAsync(
                decision,
                machine,
                null,
                database,
                live,
                loggerFactory,
                "The machine changed while the sequence was assigned. Look at it again before assigning.",
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            store.LibraryLock.Release();
        }
    }

    private static async Task<Results<Ok<MachineSummary>, NotFound, ProblemHttpResult, ValidationProblem>> EndCurrentAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        DeploymentService deployments,
        LiveNotifier live,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return TypedResults.NotFound();
        }

        DeploymentState? before = (await deployments.ActiveAsync(machine, cancellationToken).ConfigureAwait(false))?.State;

        DeploymentDecision decision = await deployments
            .EndCurrentAsync(machine, Principals.UserId(user), user.Identity?.Name, context.Connection.RemoteIpAddress?.ToString(), cancellationToken)
            .ConfigureAwait(false);

        return await CompleteAsync(
            decision,
            machine,
            before,
            database,
            live,
            loggerFactory,
            "The run changed while it was being stopped. Look at the machine again.",
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Results<Ok<MachineSummary>, NotFound, ProblemHttpResult, ValidationProblem>> CompleteAsync(
        DeploymentDecision decision,
        Machine machine,
        DeploymentState? before,
        DdtDbContext database,
        LiveNotifier live,
        ILoggerFactory loggerFactory,
        string conflict,
        CancellationToken cancellationToken)
    {
        switch (decision.Outcome)
        {
            case DeploymentOutcome.NotFound:
                return TypedResults.Problem(title: decision.Reason, statusCode: StatusCodes.Status404NotFound);
            case DeploymentOutcome.Conflict:
                return TypedResults.Problem(title: decision.Reason, statusCode: StatusCodes.Status409Conflict);
            case DeploymentOutcome.Invalid:
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [decision.Field!] = [decision.Reason!] });
        }

        Deployment deployment = decision.Deployment!;
        IReadOnlyList<DeploymentStep> changedSteps = RunReports.ChangedSteps(database);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Problem(title: conflict, statusCode: StatusCodes.Status409Conflict);
        }

        DeploymentLog.Changed(loggerFactory.CreateLogger(typeof(MachineEndpoints)), deployment, before);
        live.MachineChanged(machine, deployment);
        live.RunStepsChanged(machine.Id, changedSteps);

        return TypedResults.Ok(MachineSummaries.From(machine, deployment));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RemoveAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return TypedResults.NotFound();
        }

        // A rejected machine stays rejected however often it registers, so removing it is the only way back: it then
        // registers as a new machine at its next netboot.
        if (!IsStray(machine) && machine.State != MachineState.Rejected)
        {
            return TypedResults.Problem(
                title: "Only a rejected machine, or a waiting machine that was never approved and has no assigned run, can be removed.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return await RemoveStraysAsync([machine], user, context, database, live, timeProvider, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RemoveWaitingFromAsync(
        string waitingFrom,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        List<Machine> machines = await database.Machines
            .Where(m => m.State == MachineState.Pending
                && m.FirstApprovedUtc == null
                && m.ActiveDeploymentId == null
                && m.FirstSeenAddress == waitingFrom)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await RemoveStraysAsync(machines, user, context, database, live, timeProvider, cancellationToken).ConfigureAwait(false);
    }

    // A waiting machine with an assigned run waits on purpose, for a sign-in or a zero touch netboot.
    private static bool IsStray(Machine machine) =>
        machine.State == MachineState.Pending && machine.FirstApprovedUtc is null && machine.ActiveDeploymentId is null;

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RemoveStraysAsync(
        List<Machine> machines,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        foreach (Machine machine in machines)
        {
            database.AuditEvents.Add(new AuditEvent
            {
                OccurredUtc = now,
                Action = AuditActions.MachineRemoved,
                ActorUserId = Principals.UserId(user),
                ActorName = user.Identity?.Name,
                SubjectId = machine.Id.ToString("D"),
                SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
                Detail = machine.State == MachineState.Rejected
                    ? $"Rejected, first seen {machine.FirstSeenUtc:u} from {machine.FirstSeenAddress}."
                    : $"Waiting since {machine.FirstSeenUtc:u} from {machine.FirstSeenAddress}.",
            });
        }

        database.Machines.RemoveRange(machines);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Problem(
                title: "A machine changed while it was being removed. Look at it again before removing it.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (machines.Count > 0)
        {
            live.MachinesRemoved();
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MachineSummary>, NotFound, ProblemHttpResult>> TransitionAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        DeploymentService deployments,
        LiveNotifier live,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        string action,
        Func<Machine, string?> refusal,
        Func<Machine, Deployment?, DateTimeOffset, Task> apply,
        CancellationToken cancellationToken)
    {
        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return TypedResults.NotFound();
        }

        if (refusal(machine) is { } reason)
        {
            return TypedResults.Problem(title: reason, statusCode: StatusCodes.Status409Conflict);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        MachineState previous = machine.State;
        Deployment? active = await deployments.ActiveAsync(machine, cancellationToken).ConfigureAwait(false);
        DeploymentState? activeState = active?.State;

        await apply(machine, active, now).ConfigureAwait(false);

        database.AuditEvents.Add(new AuditEvent
        {
            OccurredUtc = now,
            Action = action,
            ActorUserId = Principals.UserId(user),
            ActorName = user.Identity?.Name,
            SubjectId = machine.Id.ToString("D"),
            SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
            Detail = machine.SignedInUserName is { } signer
                ? $"Was {previous}. Signed in at the machine by {signer}."
                : $"Was {previous}.",
        });

        IReadOnlyList<DeploymentStep> changedSteps = RunReports.ChangedSteps(database);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Problem(
                title: "The machine changed while this decision was made. Look at it again before deciding.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (active is not null)
        {
            DeploymentLog.Changed(loggerFactory.CreateLogger(typeof(MachineEndpoints)), active, activeState);
        }

        live.RunStepsChanged(machine.Id, changedSteps);

        Deployment? shown = await deployments.ShownAsync(machine, cancellationToken).ConfigureAwait(false);
        live.MachineChanged(machine, shown);

        return TypedResults.Ok(MachineSummaries.From(machine, shown));
    }
}
