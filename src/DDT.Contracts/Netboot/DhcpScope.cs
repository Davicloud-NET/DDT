// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Netboot;

// A scope of the Microsoft DHCP server on this computer, with the options 66 and 67 it has now.
public sealed record DhcpScope(string ScopeId, string Name, bool Active, string? BootServer, string? BootFile);
