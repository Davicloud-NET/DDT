// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Tokens;

public static class ApiTokenLimits
{
    public const int MaxNameLength = 64;

    public const int DefaultDays = 90;

    public const int MaxDays = 365;

    // A token in constant use gets its last use written back at most this often.
    public static readonly TimeSpan LastUsedInterval = TimeSpan.FromMinutes(1);
}
