// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DDT.Server.Authentication;

// Reads the groups of an identity from the provider, as the values of its groups claim. The map only compares strings,
// so names, paths and object ids all work.
public static class SingleSignOnGroups
{
    // Accepts one claim per group, which is what the token handler makes of a JSON array, or one claim that holds the
    // array itself. The handler may have renamed the claim through its inbound map, so that name counts too.
    public static IReadOnlyList<string> Read(ClaimsPrincipal principal, string claimType)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (string.IsNullOrWhiteSpace(claimType))
        {
            return [];
        }

        HashSet<string> types = new(StringComparer.Ordinal) { claimType };

        if (JsonWebTokenHandler.DefaultInboundClaimTypeMap.TryGetValue(claimType, out string? mapped))
        {
            types.Add(mapped);
        }

        List<string> groups = [];

        foreach (Claim claim in principal.Claims.Where(claim => types.Contains(claim.Type)))
        {
            groups.AddRange(Values(claim.Value));
        }

        return [.. groups.Distinct(StringComparer.Ordinal)];
    }

    // The OpenID Connect handler only keeps the userinfo members it maps, and the groups aren't among them. Some
    // providers only send the groups there.
    public static void CopyFromUserInformation(JsonElement userInformation, ClaimsIdentity identity, string claimType, string issuer)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (string.IsNullOrWhiteSpace(claimType)
            || userInformation.ValueKind != JsonValueKind.Object
            || !userInformation.TryGetProperty(claimType, out JsonElement groups))
        {
            return;
        }

        IEnumerable<string?> values = groups.ValueKind switch
        {
            JsonValueKind.Array => groups.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()),
            JsonValueKind.String => [groups.GetString()],
            _ => [],
        };

        foreach (string value in values.OfType<string>())
        {
            if (!identity.HasClaim(claimType, value))
            {
                identity.AddClaim(new Claim(claimType, value, ClaimValueTypes.String, issuer));
            }
        }
    }

    private static IEnumerable<string> Values(string value)
    {
        string trimmed = value.Trim();

        if (trimmed.StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize(trimmed, SingleSignOnJsonContext.Default.StringArray)?.OfType<string>() ?? [];
            }
            catch (JsonException)
            {
            }
        }

        return trimmed.Length > 0 ? [trimmed] : [];
    }
}
