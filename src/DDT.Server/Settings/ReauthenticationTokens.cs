// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using DDT.Contracts.Settings;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Settings;

// A fresh proof of identity for fields that grant roles or trust, and for uploads that run as SYSTEM on every
// netbooting machine. Protected with the key ring, so it travels in a header, for a streamed upload as for a JSON save.
public sealed class ReauthenticationTokens(IDataProtectionProvider provider, TimeProvider timeProvider)
{
    public const string HeaderName = "X-DDT-Reauthentication";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly IDataProtector _protector = provider.CreateProtector("DDT.Settings.Reauthentication");

    // Bound to the user and their security stamp, which a new password, a new authenticator or signing out everywhere
    // changes, so none of those leaves a token behind.
    public ReauthenticationToken Issue(DdtUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        DateTimeOffset expires = timeProvider.GetUtcNow() + Lifetime;
        string payload = string.Join(
            '|',
            user.Id.ToString("D"),
            user.SecurityStamp ?? string.Empty,
            expires.UtcTicks.ToString(CultureInfo.InvariantCulture));

        return new ReauthenticationToken(_protector.Protect(payload), expires);
    }

    public async Task<bool> ValidAsync(HttpContext context, ClaimsPrincipal principal, UserManager<DdtUser> users)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(users);

        string? token = context.Request.Headers[HeaderName];

        if (string.IsNullOrEmpty(token) || Principals.UserId(principal) is not { } userId)
        {
            return false;
        }

        string payload;

        try
        {
            payload = _protector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            return false;
        }

        string[] parts = payload.Split('|');

        if (parts.Length != 3
            || !Guid.TryParse(parts[0], out Guid tokenUser)
            || tokenUser != userId
            || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long expires)
            || timeProvider.GetUtcNow().UtcTicks >= expires)
        {
            return false;
        }

        DdtUser? user = await users.FindByIdAsync(userId.ToString("D")).ConfigureAwait(false);

        return user is { IsDisabled: false } && string.Equals(user.SecurityStamp ?? string.Empty, parts[1], StringComparison.Ordinal);
    }
}
