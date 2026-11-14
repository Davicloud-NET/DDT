// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Unattend;

// IPv4 settings for one adapter. Identifier is its MAC address with dashes, which Setup matches. Addresses carry their
// prefix length, such as 10.0.4.51/24.
public sealed record StaticNetwork(string Identifier, IReadOnlyList<string> Addresses, string? Gateway, IReadOnlyList<string> DnsServers);
