// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Server.Authentication;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using DDT.Server.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// Called by DDT.Agent, never by a browser, so these sit outside the /api group and its CSRF filters. No cookie is
// involved: every request carries a bearer token.
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
            .AddEndpointFilter<AgentMachineFilter>()
            .RequireAuthorization(DdtPolicies.MachineAgent)
            .RequireRateLimiting(RateLimitPolicies.AgentMachine);

        // Needs a session token, so only an approved machine writes to the log. Anyone can get a poll token by
        // registering, and could otherwise fill the database.
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

        group.MapAgentReleaseEndpoints();

        return group;
    }

    // Boot images built before registration was opened still send their enrollment token. It's ignored.
    private static async Task<Results<Ok<AgentRegistrationResult>, ValidationProblem, ProblemHttpResult>> RegisterAsync(
        AgentRegistration registration,
        HttpContext context,
        MachineRegistrar registrar,
        ConsoleLogoStore logos,
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
                title: PhaseRules.NothingToContinue(registration.Environment),
                statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Ok(registered.Result! with
            {
                ConsoleLogoSha256 = (await logos.CurrentAsync(cancellationToken).ConfigureAwait(false))?.Sha256,
            }),
        };
    }

    private static async Task<Results<Ok<AgentNextResult>, ForbidHttpResult, UnauthorizedHttpResult, NotFound>> NextAsync(
        HttpContext context,
        MachinePolls polls,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        AgentNextResult? next = await polls
            .NextAsync(
                AgentMachineFilter.MachineOf(context),
                context.User,
                timeProvider.GetUtcNow(),
                context.Connection.RemoteIpAddress?.ToString(),
                cancellationToken)
            .ConfigureAwait(false);

        return next is null ? TypedResults.Unauthorized() : TypedResults.Ok(next);
    }

    private static async Task<Results<Ok<AgentSignInResult>, ForbidHttpResult, UnauthorizedHttpResult, NotFound, ValidationProblem>> SignInAsync(
        Guid id,
        AgentSignInRequest request,
        HttpContext context,
        MachineSignIn signIn,
        CancellationToken cancellationToken)
    {
        if (!Principals.IsMachine(context.User, id))
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

        MachineSignInOutcome outcome = await signIn
            .SignInAsync(id, request, context.User, context.Connection.RemoteIpAddress?.ToString() ?? "unknown", cancellationToken)
            .ConfigureAwait(false);

        return outcome switch
        {
            { Status: { } status } => TypedResults.Ok(new AgentSignInResult(status)),
            { Unauthorized: true } => TypedResults.Unauthorized(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<NoContent, ForbidHttpResult, ValidationProblem>> AppendLogAsync(
        Guid id,
        AgentLogBatch batch,
        HttpContext context,
        MachineLogs logs,
        CancellationToken cancellationToken)
    {
        if (!Principals.IsMachine(context.User, id))
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

        await logs.AppendAsync(id, batch, cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }
}
