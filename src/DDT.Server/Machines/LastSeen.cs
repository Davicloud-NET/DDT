// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

public static class LastSeen
{
    // A poll or a report only records that the machine was seen, and a write plus a push per machine every ten
    // seconds buys nothing an operator can see. True when the machine changed and needs saving.
    public static bool Record(Machine machine, DateTimeOffset now, string? address)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (now - machine.LastSeenUtc < MachineLogLimits.LastSeenResolution && address == machine.LastSeenAddress)
        {
            return false;
        }

        machine.LastSeenUtc = now;
        machine.LastSeenAddress = address;

        return true;
    }
}
