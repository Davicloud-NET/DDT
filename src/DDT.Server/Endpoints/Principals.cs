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

    // The API token a user's request was authenticated by, null for the session cookie.
    public static Guid? ApiTokenId(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return Guid.TryParse(user.FindFirstValue(DdtClaimTypes.ApiTokenId), out Guid id) ? id : null;
    }

    public static string? ApiTokenName(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.FindFirstValue(DdtClaimTypes.ApiTokenName);
    }

    // The name to record for who acted: the user's, and the token's beside it when the request came with one, such as
    // alice (token build-server).
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

    // The token was checked against an earlier read. Registered again, rejected or stopped since then, the machine
    // belongs to a newer generation whose tokens and content this caller must not receive.
    public static bool HoldsCurrentGeneration(ClaimsPrincipal user, Machine machine)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(machine);

        return machine.TokenGeneration.ToString(CultureInfo.InvariantCulture) == user.FindFirstValue(DdtClaimTypes.TokenGeneration);
    }
}
