// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Server.Records;

// A BitLocker recovery password: 8 groups of 6 digits, each group 11 times a 16-bit number.
public static class RecoveryPasswords
{
    private const int Groups = 8;
    private const int GroupLength = 6;

    public static bool IsWellFormed(string? password)
    {
        if (password is not { Length: RecordLimits.PasswordLength })
        {
            return false;
        }

        for (int group = 0; group < Groups; group++)
        {
            int start = group * (GroupLength + 1);

            if (group > 0 && password[start - 1] != '-')
            {
                return false;
            }

            // NumberStyles.None takes digits only: no sign, no space.
            if (!int.TryParse(password.AsSpan(start, GroupLength), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                || value % 11 != 0 || value / 11 > ushort.MaxValue)
            {
                return false;
            }
        }

        return true;
    }
}
