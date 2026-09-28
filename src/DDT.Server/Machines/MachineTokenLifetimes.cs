// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

public static class MachineTokenLifetimes
{
    // Short enough that a stolen token is worth little. A multi gigabyte image copy doesn't need one token to last that
    // long, because the agent refreshes it.
    public static readonly TimeSpan Session = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Poll = TimeSpan.FromMinutes(60);

    // How long the server may be unreachable before an approved machine has to be approved again.
    public static readonly TimeSpan Resume = TimeSpan.FromHours(24);

    // A run can stop for a weekend, at Windows setup or with the machine switched off, and still continue.
    public static readonly TimeSpan Run = TimeSpan.FromDays(7);
}
