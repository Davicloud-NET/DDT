// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// What kind of computer a machine is, so the web UI can show it with a matching icon. The server tells it from what the
// firmware reports, see DDT.Core.Machines.DeviceKinds; Unknown when that says nothing, as for an agent too old to report
// its chassis type.
public enum DeviceKind
{
    Unknown,
    Laptop,
    Desktop,
    Tablet,
    Server,
    Virtual,
}
