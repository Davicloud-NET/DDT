// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Data;

public static class StoredText
{
    // PostgreSQL text can't hold a NUL and refuses a value longer than its column. An agent whose report is refused
    // for either reason would send it again forever. Returns null for an empty value.
    public static string? Bound(string? value, int maxLength)
    {
        string? text = value?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
