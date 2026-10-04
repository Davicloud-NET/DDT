// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace DDT.Server.BootImage;

// The token a builder carries. It uploads one boot image and does nothing else: no API call takes it. Whether it
// was used already is in the audit log, which every host of the server reads.
public sealed class BuilderTokens(IDataProtectionProvider provider, TimeProvider timeProvider)
{
    // The request header the builder sends it in. Not Authorization, which is an API token's.
    public const string HeaderName = "X-DDT-Builder-Token";

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    private readonly IDataProtector _protector = provider.CreateProtector("DDT.BootImageBuilder");

    public (string Token, BuilderToken Payload) Issue(string issuedBy)
    {
        BuilderToken payload = new(Guid.CreateVersion7(), issuedBy, timeProvider.GetUtcNow() + Lifetime);

        return (_protector.Protect(JsonSerializer.Serialize(payload, BuilderJsonContext.Default.BuilderToken)), payload);
    }

    // Null for a token that is none of this server's, or has expired.
    public BuilderToken? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            BuilderToken? payload = JsonSerializer.Deserialize(_protector.Unprotect(token), BuilderJsonContext.Default.BuilderToken);

            return payload is not null && payload.ExpiresUtc > timeProvider.GetUtcNow() ? payload : null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}
