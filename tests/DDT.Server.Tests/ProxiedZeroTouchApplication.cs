// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Tests;

// The zero touch networks of ZeroTouchApplication, reached through the proxies of ProxiedApplication.
public sealed class ProxiedZeroTouchApplication() : SettingsApplication(
    ("DDT:Machines:ZeroTouchNetworks", ZeroTouchApplication.Networks),
    ("DDT:ForwardedHeaders:KnownProxies", $"{ProxiedApplication.Proxy}, {ProxiedApplication.Ipv6Proxy}"),
    ("DDT:ForwardedHeaders:KnownNetworks", ProxiedApplication.ProxyNetwork));
