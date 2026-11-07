// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// The agents that netbooting machines download: the one for Windows PE, and the one for the Linux boot environment.
// Every machine runs its agent as SYSTEM or as root before anyone has authorized the machine, and only checks that it
// got what the server announced. So an upload needs a fresh proof of identity.
public static class SettingsAgentEndpoints
{
    public static RouteGroupBuilder MapSettingsAgentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        Map(group, "/agent", AgentUpload.Windows);
        Map(group, "/agent/linux", AgentUpload.Linux);

        return group;
    }

    private static void Map(RouteGroupBuilder group, string route, AgentUpload agent)
    {
        group.MapGet(route, (ReleaseUploads uploads, CancellationToken cancellationToken) => ReadAsync(agent, uploads, cancellationToken))
            .RequireAuthorization(DdtPolicies.Administrator);
        group.MapPut(
                route + "/binary",
                ([AsParameters] SettingsCaller caller, ReleaseUploads uploads, CancellationToken cancellationToken) =>
                    UploadAsync(agent, caller, uploads, cancellationToken))
            .RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete(
                route + "/binary",
                ([AsParameters] SettingsCaller caller, ReleaseUploads uploads, CancellationToken cancellationToken) =>
                    RemoveAsync(agent, caller, uploads, cancellationToken))
            .RequireAuthorization(DdtPolicies.Administrator);
    }

    private static async Task<Ok<AgentBinaryView>> ReadAsync(AgentUpload agent, ReleaseUploads uploads, CancellationToken cancellationToken) =>
        TypedResults.Ok(await uploads.AgentAsync(agent, cancellationToken).ConfigureAwait(false));

    // No fresh proof of identity: it only takes machines back to the agent the server came with, or the one they
    // started with. 404 when no agent was uploaded.
    private static async Task<Results<Ok<AgentBinaryView>, NotFound>> RemoveAsync(
        AgentUpload agent,
        SettingsCaller caller,
        ReleaseUploads uploads,
        CancellationToken cancellationToken) =>
        await uploads.RemoveAgentAsync(agent, caller.Actor, cancellationToken).ConfigureAwait(false) is { } view
            ? TypedResults.Ok(view)
            : TypedResults.NotFound();

    private static async Task<IResult> UploadAsync(AgentUpload agent, SettingsCaller caller, ReleaseUploads uploads, CancellationToken cancellationToken)
    {
        if (uploads.AgentConfigured(agent))
        {
            return ServerProblems.Problem(
                (agent.IsLinux ? ServerMessages.SettingsLinuxAgentConfigured : ServerMessages.SettingsAgentConfigured).With(),
                StatusCodes.Status409Conflict);
        }

        if (!await caller.ReauthenticatedAsync().ConfigureAwait(false))
        {
            return SettingsEndpoints.Reauthenticate(["agent"]);
        }

        if (caller.Context.Request.ContentLength > AgentReleaseStore.MaxBytes)
        {
            return TooLarge();
        }

        if (caller.Context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = AgentReleaseStore.MaxBytes;
        }

        (ReleaseUploadStatus status, AgentBinaryView? uploaded) = await uploads
            .UploadAgentAsync(agent, caller.Context.Request.Body, caller.Actor, cancellationToken)
            .ConfigureAwait(false);

        return (status, uploaded) switch
        {
            (_, { } view) => TypedResults.Ok(view),
            (ReleaseUploadStatus.TooLarge, _) => TooLarge(),
            _ => ServerProblems.Validation(
                "binary",
                (agent.IsLinux ? ServerMessages.SettingsLinuxAgentNotExecutable : ServerMessages.SettingsAgentNotExecutable).With()),
        };
    }

    private static ProblemHttpResult TooLarge() =>
        ServerProblems.Problem(
            ServerMessages.SettingsAgentTooLarge.With("max", AgentReleaseStore.MaxBytes / (1024 * 1024)),
            StatusCodes.Status413PayloadTooLarge);
}
