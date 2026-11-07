// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

// Whether a machine's restart was recorded, or why not. MayNotPick: nobody who may deploy is signed in at it.
public sealed record EnvironmentSwitchOutcome(string? Refusal, bool MayNotPick = false)
{
    public static EnvironmentSwitchOutcome Recorded { get; } = new(Refusal: null);
}
