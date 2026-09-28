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

// The agent netbooting machines download. Every one runs it as SYSTEM before anyone authorized it, and checks only that
// it got what the server announced, so an upload needs a fresh proof of identity.
public static class SettingsAgentEndpoints
{
    public static RouteGroupBuilder MapSettingsAgentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/agent", ReadAgentAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPut("/agent/binary", UploadAgentAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<AgentBinaryView>> ReadAgentAsync(ReleaseUploads uploads, CancellationToken cancellationToken) =>
        TypedResults.Ok(await uploads.AgentAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> UploadAgentAsync(
        [AsParameters] SettingsCaller caller,
        ReleaseUploads uploads,
        CancellationToken cancellationToken)
    {
        if (uploads.AgentConfigured)
        {
            return ServerProblems.Problem(ServerMessages.SettingsAgentConfigured.With(), StatusCodes.Status409Conflict);
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
            .UploadAgentAsync(caller.Context.Request.Body, caller.Actor, cancellationToken)
            .ConfigureAwait(false);

        return (status, uploaded) switch
        {
            (_, { } view) => TypedResults.Ok(view),
            (ReleaseUploadStatus.TooLarge, _) => TooLarge(),
            _ => ServerProblems.Validation("binary", ServerMessages.SettingsAgentNotExecutable.With()),
        };
    }

    private static ProblemHttpResult TooLarge() =>
        ServerProblems.Problem(
            ServerMessages.SettingsAgentTooLarge.With("max", AgentReleaseStore.MaxBytes / (1024 * 1024)),
            StatusCodes.Status413PayloadTooLarge);
}
