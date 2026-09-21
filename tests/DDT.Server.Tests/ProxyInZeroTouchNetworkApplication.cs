// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Tests;

// The proxies of ProxiedApplication inside the zero touch networks, the overlap the README warns against.
public sealed class ProxyInZeroTouchNetworkApplication() : SettingsApplication(
    ("DDT:Machines:ZeroTouchNetworks", "192.0.2.0/24, 198.51.100.0/24, 2001:db8::/64"),
    ("DDT:ForwardedHeaders:KnownProxies", $"{ProxiedApplication.Proxy}, {ProxiedApplication.Ipv6Proxy}"),
    ("DDT:ForwardedHeaders:KnownNetworks", ProxiedApplication.ProxyNetwork));
