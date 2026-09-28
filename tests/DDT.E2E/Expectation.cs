// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.E2E;

// What Eventually waits for, how long it may take, and what it shows when time runs out.
internal sealed record Expectation(string What, TimeSpan Timeout, Func<string> Diagnostics)
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(100);
}
