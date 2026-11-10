// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Server.Machines;
using DDT.Server.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// What an agent takes from the server before it registers: a newer agent, the graphical console and its logo.
public static class AgentReleaseEndpoints
{
    public static RouteGroupBuilder MapAgentReleaseEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // The agent every netbooting machine switches to. Anonymous, because the same binary is in every boot
        // image anyway.
        group.MapGet("/release", GetReleaseAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentRelease);

        group.MapGet("/release/binary", GetReleaseBinaryAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentDownload);

        // The agent built for the platform the route names, which the agent for Linux asks for. Anonymous like the
        // routes above, and under the same limits.
        group.MapGet("/releases/{platform}", GetPlatformReleaseAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentRelease);

        group.MapGet("/releases/{platform}/binary", GetPlatformReleaseBinaryAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentDownload);

        // The graphical console those agents show. It's anonymous like the agent, for the same reason.
        group.MapGet("/release/console", GetConsoleReleaseAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentRelease);

        group.MapGet("/release/console/{name}", GetConsoleFileAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentDownload);

        // The console for the platform the route names, which the agent for Linux asks for.
        group.MapGet("/releases/{platform}/console", GetPlatformConsoleReleaseAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentRelease);

        group.MapGet("/releases/{platform}/console/{name}", GetPlatformConsoleFileAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentDownload);

        // The logo that console shows. Every registration answer names its hash.
        group.MapGet("/console/logo", GetConsoleLogo)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AgentRelease);

        return group;
    }

    private static Task<Results<Ok<AgentRelease>, NotFound>> GetReleaseAsync(AgentReleaseStores releases, CancellationToken cancellationToken) =>
        ReleaseAsync(releases.Windows, cancellationToken);

    private static Task<Results<FileStreamHttpResult, NotFound>> GetReleaseBinaryAsync(AgentReleaseStores releases, CancellationToken cancellationToken) =>
        BinaryAsync(releases.Windows, cancellationToken);

    private static Task<Results<Ok<AgentRelease>, NotFound>> GetPlatformReleaseAsync(
        string platform,
        AgentReleaseStores releases,
        CancellationToken cancellationToken) =>
        ReleaseAsync(releases.Of(platform), cancellationToken);

    private static Task<Results<FileStreamHttpResult, NotFound>> GetPlatformReleaseBinaryAsync(
        string platform,
        AgentReleaseStores releases,
        CancellationToken cancellationToken) =>
        BinaryAsync(releases.Of(platform), cancellationToken);

    // 404 for a platform no agent is built for, and for one whose agent the server does not have.
    private static async Task<Results<Ok<AgentRelease>, NotFound>> ReleaseAsync(AgentReleaseStore? releases, CancellationToken cancellationToken) =>
        releases is not null && await releases.CurrentAsync(cancellationToken).ConfigureAwait(false) is { } release
            ? TypedResults.Ok(release)
            : TypedResults.NotFound();

    // Not PhysicalFile, which opens without FileShare.Delete: an upload then can't replace the agent on Windows while
    // a machine downloads it.
    private static async Task<Results<FileStreamHttpResult, NotFound>> BinaryAsync(AgentReleaseStore? releases, CancellationToken cancellationToken)
    {
        if (releases is null || await releases.OfferedAsync(cancellationToken).ConfigureAwait(false) is not { } agent)
        {
            return TypedResults.NotFound();
        }

        try
        {
            return TypedResults.File(FileReplacement.OpenRead(agent.Path), "application/octet-stream");
        }
        catch (FileNotFoundException)
        {
            return TypedResults.NotFound();
        }
    }

    private static Results<FileStreamHttpResult, NotFound> GetConsoleLogo(ConsoleLogoStore logos) =>
        logos.Open() is { } logo ? TypedResults.File(logo, "image/png") : TypedResults.NotFound();

    private static Task<Results<Ok<ConsoleRelease>, NotFound>> GetConsoleReleaseAsync(
        ConsoleReleaseStores consoles,
        CancellationToken cancellationToken) =>
        ConsoleReleaseAsync(consoles.Windows, cancellationToken);

    private static Task<Results<PushStreamHttpResult, NotFound>> GetConsoleFileAsync(
        string name,
        HttpContext context,
        ConsoleReleaseStores consoles,
        CancellationToken cancellationToken) =>
        ConsoleFileAsync(consoles.Windows, name, context, cancellationToken);

    private static Task<Results<Ok<ConsoleRelease>, NotFound>> GetPlatformConsoleReleaseAsync(
        string platform,
        ConsoleReleaseStores consoles,
        CancellationToken cancellationToken) =>
        ConsoleReleaseAsync(consoles.Of(platform), cancellationToken);

    private static Task<Results<PushStreamHttpResult, NotFound>> GetPlatformConsoleFileAsync(
        string platform,
        string name,
        HttpContext context,
        ConsoleReleaseStores consoles,
        CancellationToken cancellationToken) =>
        ConsoleFileAsync(consoles.Of(platform), name, context, cancellationToken);

    // 404 for a platform no console is built for, and for one whose console the server does not have.
    private static async Task<Results<Ok<ConsoleRelease>, NotFound>> ConsoleReleaseAsync(ConsoleReleaseStore? consoles, CancellationToken cancellationToken) =>
        consoles is not null && await consoles.CurrentAsync(cancellationToken).ConfigureAwait(false) is { } release
            ? TypedResults.Ok(release)
            : TypedResults.NotFound();

    // Only the files the release names, with the length it names, so the agent's download ends where the file does.
    private static async Task<Results<PushStreamHttpResult, NotFound>> ConsoleFileAsync(
        ConsoleReleaseStore? consoles,
        string name,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        StoredConsole? console = consoles is null ? null : await consoles.OfferedAsync(cancellationToken).ConfigureAwait(false);

        if (console?.Release.Files.FirstOrDefault(file => file.Name == name) is not { } file)
        {
            return TypedResults.NotFound();
        }

        context.Response.ContentLength = file.Size;

        return TypedResults.Stream(
            body => ConsoleReleaseStore.CopyFileAsync(console, file.Name, body, context.RequestAborted),
            "application/octet-stream");
    }
}
