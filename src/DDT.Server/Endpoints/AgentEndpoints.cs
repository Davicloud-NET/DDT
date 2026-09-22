// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IdentitySignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace DDT.Server.Endpoints;

// Called by DDT.Agent, never by a browser, so these sit outside the /api group and its CSRF filters.
// No cookie is involved: every request carries a bearer token.
public static class AgentEndpoints
{
    public static RouteGroupBuilder MapAgentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Open to anyone who reaches the server, so the body is bounded before anything reads it. Nothing is
        // released to a registered machine until someone authorizes it.
        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentRegistration)
            .WithMetadata(new RequestSizeLimitAttribute(MachineLogLimits.MaxRegistrationBytes));

        group.MapGet("/{id:guid}/next", NextAsync)
            .RequireAuthorization(DdtPolicies.MachineAgent)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine);

        // A session token, so only an approved machine writes to the log. Anyone can get a poll token by
        // registering, and would otherwise be able to fill the database.
        group.MapPost("/{id:guid}/log", AppendLogAsync)
            .RequireAuthorization(DdtPolicies.Machine)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine)
            .WithMetadata(new RequestSizeLimitAttribute(MachineLogLimits.MaxRequestBytes));

        // Anyone who registers a machine can reach this to guess passwords, exactly like the web sign in page,
        // so it shares that page's limit per address as well as the account lockout.
        group.MapPost("/{id:guid}/sign-in", SignInAsync)
            .RequireAuthorization(DdtPolicies.MachineAgent)
            .RequireRateLimiting(RateLimitPolicies.SignIn)
            .WithMetadata(new RequestSizeLimitAttribute(MachineLogLimits.MaxSignInBytes));

        // The agent every netbooting machine switches to. Anonymous, because the same binary is in every boot
        // image anyway.
        group.MapGet("/release", GetReleaseAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentRelease);

        group.MapGet("/release/binary", GetReleaseBinary)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentDownload);

        return group;
    }

    // Boot images built before registration was opened still send their enrollment token; it is ignored.
    private static async Task<Results<Ok<AgentRegistrationResult>, ValidationProblem, ProblemHttpResult>> RegisterAsync(
        AgentRegistration registration,
        HttpContext context,
        MachineRegistrar registrar,
        CancellationToken cancellationToken)
    {
        if (!RegistrationValidator.TryNormalise(registration, out NormalisedRegistration? normalised, out string error))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["registration"] = [error] });
        }

        MachineRegistration registered = await registrar
            .RegisterAsync(normalised!, context.Connection.RemoteIpAddress, cancellationToken)
            .ConfigureAwait(false);

        return registered.Refusal switch
        {
            RegistrationRefusal.TooManyWaiting => TypedResults.Problem(
                title: "Too many machines are waiting to be authorized. Approve or remove some on the Machines page.",
                statusCode: StatusCodes.Status429TooManyRequests),
            RegistrationRefusal.NothingToContinue => TypedResults.Problem(
                title: "This machine has no run for the DDT service to continue. The service removes itself.",
                statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Ok(registered.Result!),
        };
    }

    private static async Task<Results<Ok<AgentRelease>, NotFound>> GetReleaseAsync(
        AgentReleaseStore releases,
        CancellationToken cancellationToken)
    {
        AgentRelease? release = await releases.CurrentAsync(cancellationToken).ConfigureAwait(false);

        return release is null ? TypedResults.NotFound() : TypedResults.Ok(release);
    }

    private static Results<PhysicalFileHttpResult, NotFound> GetReleaseBinary(AgentReleaseStore releases)
    {
        string path = releases.BinaryPath;

        return File.Exists(path)
            ? TypedResults.PhysicalFile(path, "application/octet-stream")
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<AgentNextResult>, ForbidHttpResult, UnauthorizedHttpResult, NotFound>> NextAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        MachineTokenService tokens,
        MachineRegistrar registrar,
        DeploymentService deployments,
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

        if (!Principals.HoldsCurrentGeneration(user, machine))
        {
            return TypedResults.Unauthorized();
        }

        if (LastSeen.Record(machine, timeProvider.GetUtcNow(), context.Connection.RemoteIpAddress?.ToString()))
        {
            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                live.MachineChanged(machine, await deployments.ShownAsync(machine, cancellationToken).ConfigureAwait(false));
            }
            catch (DbUpdateConcurrencyException)
            {
                // Approved, rejected, assigned an image or registered again since this token was checked. Last
                // seen can wait for the next poll; the answer has to reflect what is stored now.
                await database.Entry(machine).ReloadAsync(cancellationToken).ConfigureAwait(false);

                if (!Principals.HoldsCurrentGeneration(user, machine))
                {
                    return TypedResults.Unauthorized();
                }
            }
        }

        // A waiting machine learns nothing about what it will be given: anyone can register as it. An agent from
        // before task sequences is never given an image deployment: this server creates none.
        bool authorized = machine.State is MachineState.Approved or MachineState.Deploying or MachineState.Failed;
        Deployment? active = authorized ? await deployments.ActiveAsync(machine, cancellationToken).ConfigureAwait(false) : null;
        AgentRun? run = active is null ? null : await deployments.HandOverAsync(machine, active, cancellationToken).ConfigureAwait(false);
        bool canPick = await deployments.CanPickAsync(machine, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new AgentNextResult(
            machine.State,
            registrar.CurrentToken(machine),
            tokens.Issue(machine, MachineTokenPurpose.Resume),
            MachineRegistrar.PollAfterSeconds,
            machine.SignedInUserName,
            Deployment: null,
            CanPickImage: false,
            DomainConfigured: authorized && deployments.DomainConfigured,
            AssignedName: authorized ? machine.AssignedName : null,
            Run: run,
            CanPickSequence: canPick,
            SuggestedSequenceId: canPick ? await deployments.SuggestedAsync(machine, cancellationToken).ConfigureAwait(false) : null));
    }

    private static async Task<Results<Ok<AgentSignInResult>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ValidationProblem>> SignInAsync(
        Guid id,
        AgentSignInRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        CredentialVerifier credentials,
        UserManager<DdtUser> users,
        IOptions<MachineOptions> options,
        DeploymentService deployments,
        LiveNotifier live,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!Principals.IsMachine(user, id))
        {
            return TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        if (string.IsNullOrWhiteSpace(request.UserName)
            || string.IsNullOrEmpty(request.Password)
            || request.UserName.Length > MachineLogLimits.MaxSignInFieldLength
            || request.Password.Length > MachineLogLimits.MaxSignInFieldLength
            || request.TwoFactorCode?.Length > MachineLogLimits.MaxSignInFieldLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["signIn"] = ["A user name and a password are required."],
            });
        }

        // Loaded before the credentials are checked, which takes a noticeable moment, so that the concurrency
        // tokens cover it: a registration that starts the machine over meanwhile must not receive this approval.
        Machine? machine = await database.Machines
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (machine is null)
        {
            return TypedResults.NotFound();
        }

        if (!Principals.HoldsCurrentGeneration(user, machine))
        {
            return TypedResults.Unauthorized();
        }

        if (machine.State != MachineState.Pending || machine.SignedInByUserId is not null)
        {
            return TypedResults.Ok(new AgentSignInResult(AgentSignInStatus.AlreadyDecided));
        }

        ILogger logger = loggerFactory.CreateLogger(typeof(AgentEndpoints));
        string address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        (IdentitySignInResult result, DdtUser? account) = await credentials
            .VerifyAsync(request.UserName, request.Password, request.TwoFactorCode, cancellationToken)
            .ConfigureAwait(false);

        if (result.RequiresTwoFactor)
        {
            return TypedResults.Ok(new AgentSignInResult(AgentSignInStatus.RequiresTwoFactor));
        }

        if (result.IsLockedOut)
        {
            AuthLog.MachineSignInLockedOut(logger, id, request.UserName, address);

            return TypedResults.Ok(new AgentSignInResult(AgentSignInStatus.LockedOut));
        }

        if (!result.Succeeded || account is null)
        {
            AuthLog.MachineSignInFailed(logger, id, request.UserName, address);

            return TypedResults.Ok(new AgentSignInResult(AgentSignInStatus.Failed));
        }

        string userName = account.UserName ?? request.UserName;

        if (!await users.IsInRoleAsync(account, DdtRoleNames.Operator).ConfigureAwait(false)
            && !await users.IsInRoleAsync(account, DdtRoleNames.Administrator).ConfigureAwait(false))
        {
            AuthLog.MachineSignInNotPermitted(logger, userName, id, address);

            return TypedResults.Ok(new AgentSignInResult(AgentSignInStatus.NotPermitted));
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Deployment? active = await deployments.ActiveAsync(machine, cancellationToken).ConfigureAwait(false);

        machine.SignedInByUserId = account.Id;
        machine.SignedInUserName = userName;
        machine.SignedInUtc = now;
        database.AuditEvents.Add(SignInAudit(now, AuditActions.MachineSignedIn, account, userName, machine, address, "Signed in at the machine."));

        if (!options.Value.RequireWebApproval || DeploymentService.CountsAsWebApproval(active))
        {
            machine.State = MachineState.Approved;
            machine.ApprovedByUserId = account.Id;
            machine.ApprovedUtc = now;
            machine.FirstApprovedUtc ??= now;
            database.AuditEvents.Add(SignInAudit(
                now,
                AuditActions.MachineApproved,
                account,
                userName,
                machine,
                address,
                options.Value.RequireWebApproval
                    ? $"Was Pending. Signed in at the machine, which {active!.RequestedByName} had assigned {active.Title} on the web."
                    : "Was Pending. Signed in at the machine."));
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception) when (exception.Entries.Any(entry => entry.Entity is Machine))
        {
            await database.Entry(machine).ReloadAsync(cancellationToken).ConfigureAwait(false);

            return Principals.HoldsCurrentGeneration(user, machine)
                ? TypedResults.Ok(new AgentSignInResult(AgentSignInStatus.AlreadyDecided))
                : TypedResults.Unauthorized();
        }

        AuthLog.SignedInAtMachine(logger, userName, id);
        live.MachineChanged(machine, active ?? await deployments.ShownAsync(machine, cancellationToken).ConfigureAwait(false));

        return TypedResults.Ok(new AgentSignInResult(AgentSignInStatus.Succeeded));
    }

    private static AuditEvent SignInAudit(
        DateTimeOffset now,
        string action,
        DdtUser account,
        string userName,
        Machine machine,
        string address,
        string detail) => new()
        {
            OccurredUtc = now,
            Action = action,
            ActorUserId = account.Id,
            ActorMachineId = machine.Id,
            ActorName = userName,
            SubjectId = machine.Id.ToString("D"),
            SourceAddress = address,
            Detail = detail,
        };

    // Each line is tagged with the run that is active when it arrives. The agent sends what it logged before a report
    // that ends the run ahead of that report.
    private static async Task<Results<NoContent, ForbidHttpResult, ValidationProblem>> AppendLogAsync(
        Guid id,
        AgentLogBatch batch,
        ClaimsPrincipal user,
        DdtDbContext database,
        LiveNotifier live,
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
        TimeSpan skew = MachineLogClock.Skew(batch.SentUtc, received);
        Guid? runId = await database.Machines
            .Where(m => m.Id == id)
            .Select(m => m.ActiveDeploymentId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        List<MachineLogLine> added = [];

        foreach (AgentLogLine line in batch.Lines)
        {
            // PostgreSQL text cannot hold a NUL, and a batch it refuses would be resent forever.
            string message = (line.Message ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal);

            added.Add(new MachineLogLine
            {
                MachineId = id,
                TimestampUtc = MachineLogClock.Corrected(line.TimestampUtc, skew, received),
                AgentTimestampUtc = line.TimestampUtc.ToUniversalTime(),
                ReceivedUtc = received,
                Level = Enum.IsDefined(line.Level) ? line.Level : AgentLogLevel.Information,
                Message = message.Length <= MachineLogLimits.MaxMessageLength
                    ? message
                    : message[..MachineLogLimits.MaxMessageLength],
                DeploymentId = runId,
                StepId = runId is null ? null : line.StepId,
            });
        }

        database.MachineLogLines.AddRange(added);
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

        if (added.Count > 0)
        {
            live.MachineLogAppended(id, added.Max(l => l.Id));
        }

        return TypedResults.NoContent();
    }
}
