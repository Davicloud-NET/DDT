// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace DDT.Server.Tests;

// RFC 6238 as Identity's authenticator provider checks it: HMAC-SHA1 over 30 second steps, six digits. The
// provider cannot generate codes itself, so tests compute what an authenticator app would show.
internal static class Totp
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Code(string base32Key, DateTimeOffset now)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, now.ToUnixTimeSeconds() / 30);

        byte[] hash = HMACSHA1.HashData(DecodeBase32(base32Key), counter);
        int offset = hash[^1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];

        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string input)
    {
        List<byte> bytes = [];
        int buffer = 0;
        int bits = 0;

        foreach (char character in input.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | Base32Alphabet.IndexOf(character, StringComparison.Ordinal);
            bits += 5;

            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        return [.. bytes];
    }
}
