// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Agent;

internal static class Durations
{
    // Formats a timeout for a message: "30 s" below a minute, "2.5 minutes" from one minute up.
    public static string Describe(TimeSpan duration) => duration < TimeSpan.FromMinutes(1)
        ? string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:0.#} s")
        : string.Create(CultureInfo.InvariantCulture, $"{duration.TotalMinutes:0.#} minutes");
}
