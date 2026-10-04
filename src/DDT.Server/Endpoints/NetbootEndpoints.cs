// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using DDT.Contracts.Messages;
using DDT.Contracts.Netboot;
using DDT.Server.Authentication;
using DDT.Server.BootImage;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Netboot;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// Netboot next to what the server runs already: who holds the ports, options 66 and 67 on its DHCP server, and DDT in
// place of WDS or in its boot menu. Each change decides what every machine that netboots loads, so it takes the
// password again.
public static class NetbootEndpoints
{
    private const int MaxScopes = 256;

    public static RouteGroupBuilder MapNetbootEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", async (NetbootNeighbourhood neighbourhood, CancellationToken cancellationToken) =>
                TypedResults.Ok(await neighbourhood.ReadAsync(cancellationToken).ConfigureAwait(false)))
            .RequireAuthorization(DdtPolicies.Administrator);
        group.MapGet("/dhcp-scopes", ReadScopesAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/dhcp-options", SetOptionsAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/dhcp-pxe", SetPxeAsync).RequireAuthorization(DdtPolicies.Administrator);
        MapWds(group, "/wds/replace", HelperRequest.WdsReplace, AuditActions.WdsReplaced);
        MapWds(group, "/wds/restore", HelperRequest.WdsRestore, AuditActions.WdsRestored);
        MapWds(group, "/wds/boot-image", HelperRequest.WdsBootImage, AuditActions.WdsBootImageAdded);

        return group;
    }

    // Each answers with the neighbours as they are afterwards
    private static void MapWds(RouteGroupBuilder group, string route, string kind, string action) =>
        group.MapPost(route, async ([AsParameters] SettingsCaller caller, [AsParameters] NetbootServices services, CancellationToken cancellationToken) =>
                await RunAsync(new HelperRequest { Kind = kind }, (action, null), caller, services, cancellationToken).ConfigureAwait(false)
                    ?? TypedResults.Ok(await services.Neighbourhood.ReadAsync(cancellationToken).ConfigureAwait(false)))
            .RequireAuthorization(DdtPolicies.Administrator);

    private static async Task<Results<Ok<IReadOnlyList<DhcpScope>>, ProblemHttpResult>> ReadScopesAsync(NetbootHelper helper, CancellationToken cancellationToken)
    {
        if (!helper.Available)
        {
            return ServerProblems.Problem(ServerMessages.NetbootNoHelper.With(), StatusCodes.Status409Conflict);
        }

        (IReadOnlyList<DhcpScope> scopes, string? problem) = await helper.ScopesAsync(cancellationToken).ConfigureAwait(false);

        return problem is null
            ? TypedResults.Ok(scopes)
            : ServerProblems.Problem(ServerMessages.NetbootHelperFailed.With("reason", problem), StatusCodes.Status502BadGateway);
    }

    private static async Task<IResult> SetOptionsAsync(
        SetDhcpOptionsRequest request,
        [AsParameters] SettingsCaller caller,
        [AsParameters] NetbootServices services,
        CancellationToken cancellationToken)
    {
        string[] scopes = [.. (request.Scopes ?? []).Distinct(StringComparer.Ordinal)];

        if (scopes.Length is 0 or > MaxScopes || !scopes.All(scope => IPAddress.TryParse(scope, out IPAddress? address) && address.AddressFamily == AddressFamily.InterNetwork))
        {
            return ServerProblems.Validation("scopes", ServerMessages.NetbootScopes.With());
        }

        HelperRequest change = new()
        {
            Kind = HelperRequest.DhcpOptions,
            Scopes = scopes,
            BootServer = NetbootNeighbourhood.BootServer,
            BootFile = Pxe.PxeSetup.DefaultBootFile,
        };

        string detail = $"Scopes {string.Join(", ", scopes)}: {change.BootServer}, {change.BootFile}.";

        if (await RunAsync(change, (AuditActions.DhcpOptionsSet, detail), caller, services, cancellationToken).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        (IReadOnlyList<DhcpScope> now, _) = await services.Helper.ScopesAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(now);
    }

    // Option 60 on this computer's DHCP server. Answers with the neighbours as they are afterwards.
    private static async Task<IResult> SetPxeAsync(
        SetDhcpPxeRequest request,
        [AsParameters] SettingsCaller caller,
        [AsParameters] NetbootServices services,
        CancellationToken cancellationToken)
    {
        HelperRequest change = new() { Kind = request.Send ? HelperRequest.DhcpPxeOn : HelperRequest.DhcpPxeOff };
        string action = request.Send ? AuditActions.DhcpPxeOn : AuditActions.DhcpPxeOff;

        return await RunAsync(change, (action, null), caller, services, cancellationToken).ConfigureAwait(false)
            ?? TypedResults.Ok(await services.Neighbourhood.ReadAsync(cancellationToken).ConfigureAwait(false));
    }

    // Returns null when the helper did it, with its audit row saved, or the refusal.
    private static async Task<IResult?> RunAsync(
        HelperRequest request,
        (string Action, string? Detail) audit,
        SettingsCaller caller,
        NetbootServices services,
        CancellationToken cancellationToken)
    {
        if (!services.Helper.Available)
        {
            return ServerProblems.Problem(ServerMessages.NetbootNoHelper.With(), StatusCodes.Status409Conflict);
        }

        if (!await caller.ReauthenticatedAsync().ConfigureAwait(false))
        {
            return SettingsEndpoints.Reauthenticate(["netboot"]);
        }

        (_, string? problem) = await services.Helper.RunAsync(request, cancellationToken).ConfigureAwait(false);

        if (problem is not null)
        {
            return ServerProblems.Problem(ServerMessages.NetbootHelperFailed.With("reason", problem), StatusCodes.Status502BadGateway);
        }

        services.Database.AuditEvents.Add(AuditEvents.Create(audit.Action, null, caller.Actor, services.TimeProvider.GetUtcNow(), audit.Detail));
        await services.Database.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        return null;
    }
}
