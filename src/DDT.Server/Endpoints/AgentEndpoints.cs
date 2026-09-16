using System.Globalization;
using System.Security.Claims;
using DDT.Contracts.Agents;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

// Called by DDT.Agent, never by a browser, so these sit outside the /api group and its CSRF filters.
// No cookie is involved: every request carries a bearer token.
public static class AgentEndpoints
{
    private const string BearerPrefix = "Bearer ";

    public static RouteGroupBuilder MapAgentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // The enrollment token is public, so the body is bounded before anything reads it.
        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentRegistration)
            .WithMetadata(new RequestSizeLimitAttribute(MachineLogLimits.MaxRegistrationBytes));

        group.MapGet("/{id:guid}/next", NextAsync)
            .RequireAuthorization(DdtPolicies.MachineAgent)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine);

        // A session token, so only an approved machine writes to the log. A poll token comes with the public
        // enrollment token and would otherwise let anyone fill the database.
        group.MapPost("/{id:guid}/log", AppendLogAsync)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine)
            .WithMetadata(new RequestSizeLimitAttribute(MachineLogLimits.MaxRequestBytes));

        return group;
    }

    private static async Task<Results<Ok<AgentRegistrationResult>, UnauthorizedHttpResult, ValidationProblem>> RegisterAsync(
        AgentRegistration registration,
        HttpContext context,
        EnrollmentTokenService enrollmentTokens,
        MachineRegistrar registrar,
        CancellationToken cancellationToken)
    {
        string? header = context.Request.Headers.Authorization;
        string? presented = header is not null && header.StartsWith(BearerPrefix, StringComparison.Ordinal)
            ? header[BearerPrefix.Length..]
            : null;

        EnrollmentToken? enrollment = await enrollmentTokens.ValidateAsync(presented, cancellationToken).ConfigureAwait(false);

        if (enrollment is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!RegistrationValidator.TryNormalise(registration, out NormalisedRegistration? normalised, out string error))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["registration"] = [error] });
        }

        AgentRegistrationResult result = await registrar
            .RegisterAsync(normalised!, enrollment, context.Connection.RemoteIpAddress?.ToString(), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<AgentNextResult>, ForbidHttpResult, UnauthorizedHttpResult, NotFound>> NextAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        MachineTokenService tokens,
        MachineRegistrar registrar,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!Principals.IsMachine(user, id))
        {
            return TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        Machine? machine = await database.Machines
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (machine is null)
        {
            return TypedResults.NotFound();
        }

        // The token was checked against an earlier read. Registered again or rejected since then, the machine
        // belongs to a newer generation whose tokens this caller must not receive.
        if (!HoldsCurrentGeneration(user, machine))
        {
            return TypedResults.Unauthorized();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string? address = context.Connection.RemoteIpAddress?.ToString();

        if (now - machine.LastSeenUtc >= MachineLogLimits.LastSeenResolution || address != machine.LastSeenAddress)
        {
            machine.LastSeenUtc = now;
            machine.LastSeenAddress = address;

            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                live.MachineChanged(machine);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Approved, rejected or registered again since this token was checked. Last seen can wait for
                // the next poll; the answer has to reflect what is stored now.
                await database.Entry(machine).ReloadAsync(cancellationToken).ConfigureAwait(false);

                if (!HoldsCurrentGeneration(user, machine))
                {
                    return TypedResults.Unauthorized();
                }
            }
        }

        return TypedResults.Ok(new AgentNextResult(
            machine.State,
            registrar.CurrentToken(machine),
            tokens.Issue(machine, MachineTokenPurpose.Resume),
            MachineRegistrar.PollAfterSeconds));
    }

    private static bool HoldsCurrentGeneration(ClaimsPrincipal user, Machine machine) =>
        machine.TokenGeneration.ToString(CultureInfo.InvariantCulture) == user.FindFirstValue(DdtClaimTypes.TokenGeneration);

    private static async Task<Results<NoContent, ForbidHttpResult, ValidationProblem>> AppendLogAsync(
        Guid id,
        AgentLogBatch batch,
        ClaimsPrincipal user,
        DdtDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!Principals.IsMachine(user, id))
        {
            return TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        if (batch.Lines is null || batch.Lines.Count > MachineLogLimits.MaxLinesPerBatch)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["lines"] = [$"A batch holds at most {MachineLogLimits.MaxLinesPerBatch} lines."],
            });
        }

        DateTimeOffset received = timeProvider.GetUtcNow();

        foreach (AgentLogLine line in batch.Lines)
        {
            // PostgreSQL text cannot hold a NUL, and a batch it refuses would be resent forever.
            string message = (line.Message ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal);

            database.MachineLogLines.Add(new MachineLogLine
            {
                MachineId = id,
                TimestampUtc = line.TimestampUtc,
                ReceivedUtc = received,
                Level = Enum.IsDefined(line.Level) ? line.Level : AgentLogLevel.Information,
                Message = message.Length <= MachineLogLimits.MaxMessageLength
                    ? message
                    : message[..MachineLogLimits.MaxMessageLength],
            });
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Keep the newest lines. The oldest are the least useful once a machine has logged this much.
        long? cutoff = await database.MachineLogLines
            .Where(l => l.MachineId == id)
            .OrderByDescending(l => l.Id)
            .Skip(MachineLogLimits.MaxStoredLinesPerMachine)
            .Select(l => (long?)l.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (cutoff is { } oldestKept)
        {
            await database.MachineLogLines
                .Where(l => l.MachineId == id && l.Id <= oldestKept)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return TypedResults.NoContent();
    }
}
