// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// The answer to an administrator's reveal, and nowhere else.
public sealed record RecoveryKeyReveal(string Password)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() => "RecoveryKeyReveal";
}
