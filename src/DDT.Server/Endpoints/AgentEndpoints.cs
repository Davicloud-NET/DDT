// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Server.Authentication;
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

        // The agent every netbooting machine switches to. Anonymous, because the same binary is in every boot
        // image anyway.
        group.MapGet("/release", GetReleaseAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentRelease);

        group.MapGet("/release/binary", GetReleaseBinary)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentDownload);

        // The graphical console those agents show. It's anonymous like the agent, for the same reason.
        group.MapGet("/release/console", GetConsoleReleaseAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentRelease);

        group.MapGet("/release/console/{name}", GetConsoleFileAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentDownload);

        // The logo that console shows. Every registration answer names its hash.
        group.MapGet("/console/logo", GetConsoleLogo)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentRelease);

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
                title: "This machine has no run for the DDT service to continue. The service removes itself.",
                statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Ok(registered.Result! with
            {
                ConsoleLogoSha256 = (await logos.CurrentAsync(cancellationToken).ConfigureAwait(false))?.Sha256,
            }),
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

    private static Results<PhysicalFileHttpResult, NotFound> GetConsoleLogo(ConsoleLogoStore logos) =>
        File.Exists(logos.Path) ? TypedResults.PhysicalFile(logos.Path, "image/png") : TypedResults.NotFound();

    private static async Task<Results<Ok<ConsoleRelease>, NotFound>> GetConsoleReleaseAsync(
        ConsoleReleaseStore consoles,
        CancellationToken cancellationToken)
    {
        ConsoleRelease? release = await consoles.CurrentAsync(cancellationToken).ConfigureAwait(false);

        return release is null ? TypedResults.NotFound() : TypedResults.Ok(release);
    }

    // Only the files the release names, with the length it names, so the agent's download ends where the file does.
    private static async Task<Results<PushStreamHttpResult, NotFound>> GetConsoleFileAsync(
        string name,
        HttpContext context,
        ConsoleReleaseStore consoles,
        CancellationToken cancellationToken)
    {
        ConsoleRelease? release = await consoles.CurrentAsync(cancellationToken).ConfigureAwait(false);

        if (release?.Files.FirstOrDefault(file => file.Name == name) is not { } file)
        {
            return TypedResults.NotFound();
        }

        context.Response.ContentLength = file.Size;

        return TypedResults.Stream(
            body => consoles.CopyFileAsync(file.Name, body, context.RequestAborted),
            "application/octet-stream");
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
