// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Netboot;

// Whether the DHCP server of the server's computer sends option 60, PXEClient.
public sealed record SetDhcpPxeRequest(bool Send);
