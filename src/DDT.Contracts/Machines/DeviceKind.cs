// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// What kind of computer a machine is, based on what the firmware reports, so the web UI can show a matching icon.
public enum DeviceKind
{
    // The firmware doesn't say, or the agent is too old to report the chassis type.
    Unknown,
    Laptop,
    Desktop,
    Tablet,
    Server,
    Virtual,
}
