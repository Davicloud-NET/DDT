// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Netboot;

// A process that holds one of the netboot ports. Service names it where DDT knows what it is: "DDT", "DHCP" for
// Microsoft's DHCP server, "WDS" for Windows Deployment Services.
public sealed record NetbootPortOwner(int ProcessId, string Process, string? Service);
