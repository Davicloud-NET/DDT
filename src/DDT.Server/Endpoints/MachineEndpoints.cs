using System.Security.Claims;
using DDT.Contracts.Machines;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DDT.Server.Endpoints;

public static class MachineEndpoints
{
    public static RouteGroupBuilder MapMachineEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapGet("/{id:guid}/log", ReadLogAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPost("/{id:guid}/approve", ApproveAsync).RequireAuthorization(DdtPolicies.Operator);
        group.MapPost("/{id:guid}/reject", RejectAsync).RequireAuthorization(DdtPolicies.Operator);

        // Anyone who reaches the server can register machines, so an operator can throw away the ones nobody
        // vouched for, one at a time or everything waiting from one address.
        group.MapDelete("/{id:guid}", RemoveAsync).RequireAuthorization(DdtPolicies.Operator);
        group.MapDelete("/", RemoveWaitingFromAsync).RequireAuthorization(DdtPolicies.Operator);

        return group;
    }

    // SQLite cannot order by DateTimeOffset, and a fleet this size sorts in memory for nothing. The order
    // must not depend on anything a poll changes, or rows move under an operator's pointer.
    private static async Task<Ok<IReadOnlyList<MachineSummary>>> ListAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        List<Machine> machines = await database.Machines.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<MachineSummary>>(
        [
            .. machines
                .OrderBy(m => m.State == MachineState.Pending ? 0 : 1)
                .ThenByDescending(m => m.FirstSeenUtc)
                .ThenBy(m => m.Id)
                .Select(MachineSummaries.From),
        ]);
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

    private static Task<Results<Ok<MachineSummary>, NotFound, ProblemHttpResult>> ApproveAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        IOptions<MachineOptions> options,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            id,
            user,
            context,
            database,
            live,
            timeProvider,
            AuditActions.MachineApproved,
            machine => machine.State != MachineState.Pending
                ? $"The machine is {machine.State}."
                : options.Value.RequireWebApproval && machine.SignedInByUserId is null
                    ? "Nobody has signed in at this machine yet."
                    : null,
            (machine, now, userId) =>
            {
                machine.State = MachineState.Approved;
                machine.ApprovedByUserId = userId;
                machine.ApprovedUtc = now;
                machine.FirstApprovedUtc ??= now;
            },
            cancellationToken);

    private static Task<Results<Ok<MachineSummary>, NotFound, ProblemHttpResult>> RejectAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            id,
            user,
            context,
            database,
            live,
            timeProvider,
            AuditActions.MachineRejected,
            machine => machine.State is MachineState.Pending or MachineState.Approved ? null : $"The machine is {machine.State}.",
            (machine, _, _) =>
            {
                // The generation bump kills every token already issued, so a rejected machine stops
                // mid request rather than at its next token refresh.
                machine.State = MachineState.Rejected;
                machine.TokenGeneration++;
                machine.ApprovedByUserId = null;
                machine.ApprovedUtc = null;
            },
            cancellationToken);

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

        if (!IsStray(machine))
        {
            return TypedResults.Problem(
                title: "Only a machine that is waiting and was never approved can be removed.",
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
            .Where(m => m.State == MachineState.Pending && m.FirstApprovedUtc == null && m.FirstSeenAddress == waitingFrom)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await RemoveStraysAsync(machines, user, context, database, live, timeProvider, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsStray(Machine machine) =>
        machine.State == MachineState.Pending && machine.FirstApprovedUtc is null;

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
                Detail = $"Waiting since {machine.FirstSeenUtc:u} from {machine.FirstSeenAddress}.",
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
        LiveNotifier live,
        TimeProvider timeProvider,
        string action,
        Func<Machine, string?> refusal,
        Action<Machine, DateTimeOffset, Guid?> apply,
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
        Guid? userId = Principals.UserId(user);
        MachineState previous = machine.State;

        apply(machine, now, userId);

        database.AuditEvents.Add(new AuditEvent
        {
            OccurredUtc = now,
            Action = action,
            ActorUserId = userId,
            ActorName = user.Identity?.Name,
            SubjectId = machine.Id.ToString("D"),
            SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
            Detail = machine.SignedInUserName is { } signer
                ? $"Was {previous}. Signed in at the machine by {signer}."
                : $"Was {previous}.",
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

        live.MachineChanged(machine);

        return TypedResults.Ok(MachineSummaries.From(machine));
    }
}
