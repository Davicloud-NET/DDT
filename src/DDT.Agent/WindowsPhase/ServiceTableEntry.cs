// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsPhase;

// SERVICE_TABLE_ENTRYW: the service's name and its ServiceMain. A table ends with an entry of two nulls.
internal unsafe struct ServiceTableEntry
{
    public char* ServiceName;
    public delegate* unmanaged<uint, char**, void> ServiceMain;
}
