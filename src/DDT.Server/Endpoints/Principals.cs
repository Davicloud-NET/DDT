// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Security.Claims;
using DDT.Server.Authentication;
using DDT.Server.Machines;

namespace DDT.Server.Endpoints;

public static class Principals
{
    public static Guid? UserId(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null;
    }

    // The API token that authenticated a user's request, or null for the session cookie. Only the identity the token
    // handler created counts, so a claim with that name from anywhere else can't let a cookie request skip the CSRF
    // filters.
    public static Guid? ApiTokenId(ClaimsPrincipal user) =>
        Guid.TryParse(TokenIdentity(user)?.FindFirst(DdtClaimTypes.ApiTokenId)?.Value, out Guid id) ? id : null;

    public static string? ApiTokenName(ClaimsPrincipal user) => TokenIdentity(user)?.FindFirst(DdtClaimTypes.ApiTokenName)?.Value;

    // The name to record for who acted. It's the user's name, plus the token's name when the request used one, such as
    // "alice (token build-server)".
    public static string? ActorName(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return ApiTokenId(user) is null ? user.Identity?.Name : $"{user.Identity?.Name} (token {ApiTokenName(user)})";
    }

    public static bool IsMachine(ClaimsPrincipal user, Guid machineId)
    {
        ArgumentNullException.ThrowIfNull(user);

        return UserId(user) == machineId;
    }

    // The token was checked against an earlier read. If the machine was registered again, rejected or stopped since
    // then, it belongs to a newer generation. This caller must not receive that generation's tokens or content.
    public static bool HoldsCurrentGeneration(ClaimsPrincipal user, Machine machine)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(machine);

        return machine.TokenGeneration.ToString(CultureInfo.InvariantCulture) == user.FindFirstValue(DdtClaimTypes.TokenGeneration);
    }

    private static ClaimsIdentity? TokenIdentity(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.Identities.FirstOrDefault(identity => identity.AuthenticationType == DdtAuthenticationSchemes.ApiToken);
    }
}
