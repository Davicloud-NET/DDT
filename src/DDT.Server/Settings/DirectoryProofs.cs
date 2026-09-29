// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DDT.Server.Ldap;
using Microsoft.AspNetCore.DataProtection;

namespace DDT.Server.Settings;

// An LDAP test hands this token to a directory administrator whose own sign-in with the candidate values kept the
// Administrator role. The token is bound to exactly those values, and a save of them accepts it for 5 minutes.
public sealed class DirectoryProofs(IDataProtectionProvider provider, TimeProvider timeProvider)
{
    public const string HeaderName = "X-DDT-Directory-Proof";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly IDataProtector _protector = provider.CreateProtector("DDT.Settings.DirectoryProof");

    public string Issue(Guid userId, LdapOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        long expires = (timeProvider.GetUtcNow() + Lifetime).UtcTicks;

        return _protector.Protect(string.Join('|', userId.ToString("D"), Fingerprint(options), expires.ToString(CultureInfo.InvariantCulture)));
    }

    public bool Accepts(string? proof, Guid userId, LdapOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(proof))
        {
            return false;
        }

        try
        {
            string[] parts = _protector.Unprotect(proof).Split('|');

            return parts.Length == 3
                && Guid.TryParse(parts[0], out Guid proven)
                && proven == userId
                && string.Equals(parts[1], Fingerprint(options), StringComparison.Ordinal)
                && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long expires)
                && timeProvider.GetUtcNow().UtcTicks < expires;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    // Hashes everything that decides whether and as whom a directory account signs in, including the bind password.
    private static string Fingerprint(LdapOptions options)
    {
        StringBuilder text = new();

        foreach (string part in new[]
        {
            options.Host,
            options.Port.ToString(CultureInfo.InvariantCulture),
            options.Transport.ToString(),
            options.BaseDn,
            options.BindDn,
            options.BindPassword,
            options.UserFilter,
            options.ImmutableIdAttribute,
            options.ResolveNestedGroups.ToString(),
        })
        {
            text.Append(part).Append('\n');
        }

        foreach ((string group, string role) in options.GroupRoleMap.OrderBy(entry => entry.Key.ToUpperInvariant(), StringComparer.Ordinal))
        {
            text.Append(group.ToUpperInvariant()).Append('=').Append(role.ToUpperInvariant()).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
