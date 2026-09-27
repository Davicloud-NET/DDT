// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Builder;

namespace DDT.Server.Security;

// The addresses the proxies section trusts. A request that still comes from one after the middleware ran carried no
// client address the proxy added.
public sealed class ListedProxies(DdtSettings settings)
{
    // Matched as the middleware matches them, so an IPv4 client on a dual-stack socket finds its IPv4 entry.
    public bool Contains(IPAddress? address) => Contains(settings.Current, address);

    // From the snapshot the caller already read, so a decision uses one version of the settings throughout.
    public static bool Contains(SettingsSnapshot snapshot, IPAddress? address)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (address is null)
        {
            return false;
        }

        ForwardedHeadersOptions trusted = snapshot.ForwardedHeaders;

        return Lists(trusted, address) || (address.IsIPv4MappedToIPv6 && Lists(trusted, address.MapToIPv4()));
    }

    private static bool Lists(ForwardedHeadersOptions trusted, IPAddress address) =>
        trusted.KnownProxies.Contains(address) || trusted.KnownIPNetworks.Any(network => network.Contains(address));
}
