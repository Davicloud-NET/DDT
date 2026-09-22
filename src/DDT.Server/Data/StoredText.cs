// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Data;

public static class StoredText
{
    // PostgreSQL text cannot hold a NUL and refuses a value longer than its column, and an agent whose report is
    // refused for either would send it again forever. Null for nothing.
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
