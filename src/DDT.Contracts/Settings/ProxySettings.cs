// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The section proxies: the reverse proxies whose X-Forwarded-For and X-Forwarded-Proto DDT believes.
public sealed record ProxySettings(IReadOnlyList<string> KnownProxies, IReadOnlyList<string> KnownNetworks);
