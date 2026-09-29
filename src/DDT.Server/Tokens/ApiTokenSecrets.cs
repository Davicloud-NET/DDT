// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Text;

namespace DDT.Server.Tokens;

// A secret is the prefix plus 43 base62 characters. They hold 256 bits from the system's random number generator. The
// prefix lets secret scanners, such as GitHub's, recognise a token that leaked into a repository or a log.
public static class ApiTokenSecrets
{
    public const string Prefix = "ddt_";

    public const int HintLength = 4;

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int RandomLength = 43;

    public static string Create() => Prefix + RandomNumberGenerator.GetString(Alphabet, RandomLength);

    // A plain hash is enough. The secret is random and long, so there's nothing guessable for a slow hash to protect.
    public static string Hash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    public static string Hint(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return secret[^HintLength..];
    }

    // Checked before the database is asked, so a machine token or garbage costs no query.
    public static bool LooksLikeOne(string value) =>
        value.Length == Prefix.Length + RandomLength
        && value.StartsWith(Prefix, StringComparison.Ordinal)
        && !value.AsSpan(Prefix.Length).ContainsAnyExcept(Alphabet);
}
