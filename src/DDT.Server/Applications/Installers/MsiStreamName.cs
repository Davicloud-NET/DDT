// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Server.Applications.Installers;

// Decodes the names Windows Installer gives its streams. Compound file names hold at most 31 characters, so it packs
// two characters of [0-9A-Za-z._] into one in 0x3800-0x47FF, one into 0x4800-0x483F, and marks a table with 0x4840.
internal static class MsiStreamName
{
    public const char TablePrefix = '!';

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz._";
    private const char PairStart = '㠀';
    private const char SingleStart = '䠀';
    private const char TableMarker = '䡀';

    public static string Decode(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        StringBuilder decoded = new(name.Length * 2);

        foreach (char character in name)
        {
            if (character is >= PairStart and < SingleStart)
            {
                int pair = character - PairStart;
                decoded.Append(Alphabet[pair & 0x3F]).Append(Alphabet[pair >> 6]);
            }
            else if (character is >= SingleStart and < TableMarker)
            {
                decoded.Append(Alphabet[character - SingleStart]);
            }
            else if (character == TableMarker)
            {
                decoded.Append(TablePrefix);
            }
            else
            {
                decoded.Append(character);
            }
        }

        return decoded.ToString();
    }
}
