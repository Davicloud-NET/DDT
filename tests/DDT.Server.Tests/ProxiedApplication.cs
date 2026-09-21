// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;

namespace DDT.Server.Tests;

public sealed class ProxiedApplication : DdtApplication
{
    public const string Proxy = "192.0.2.10";

    public const string Ipv6Proxy = "2001:db8::10";

    public const string ProxyNetwork = "198.51.100.0/24";

    // An agent whose requests arrive from connection, the proxy unless another is named, carrying X-Forwarded-For.
    public static AgentClient Agent(DdtApplication host, string forwardedFor, string connection = Proxy)
    {
        ArgumentNullException.ThrowIfNull(host);

        HttpClient http = host.CreateDefaultClient();
        http.DefaultRequestHeaders.Add(ForwardedHeadersDefaults.XForwardedForHeaderName, forwardedFor);

        return new AgentClient(http, connection);
    }

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("DDT:ForwardedHeaders:KnownProxies", $"{Proxy}, {Ipv6Proxy}");
        builder.UseSetting("DDT:ForwardedHeaders:KnownNetworks", ProxyNetwork);
    }
}
