// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

// Whether a machine's restart into the other boot environment was recorded. Refusal says why not, for the technician.
// MayNotPick means nobody who may deploy is signed in at the machine.
public sealed record EnvironmentSwitchOutcome(string? Refusal, bool MayNotPick = false)
{
    public static EnvironmentSwitchOutcome Recorded { get; } = new(Refusal: null);
}
