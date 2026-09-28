// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;

namespace DDT.Agent.WindowsPhase;

// A password for DDT's session account that nobody is told.
internal static class SessionPassword
{
    // 40 characters, at least one of each kind, as a local password policy may ask for.
    public static string New()
    {
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        const string symbols = "!#%+-.=?@_";

        char[] password = [
            .. RandomNumberGenerator.GetItems<char>(lower + upper + digits + symbols, 36),
            RandomNumberGenerator.GetItems<char>(lower, 1)[0],
            RandomNumberGenerator.GetItems<char>(upper, 1)[0],
            RandomNumberGenerator.GetItems<char>(digits, 1)[0],
            RandomNumberGenerator.GetItems<char>(symbols, 1)[0],
        ];
        RandomNumberGenerator.Shuffle<char>(password);

        return new string(password);
    }
}
