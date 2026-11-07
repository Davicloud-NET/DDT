// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Extensions.Configuration;

namespace DDT.Server.Certificates;

// Where machines reach this server: the DNS name its certificate carries, and the port its HTTPS endpoint listens on.
public sealed class ServerAddress(IConfiguration configuration)
{
    private const int DefaultPort = 8443;

    public int Port =>
        configuration["Kestrel:Endpoints:Https:Url"] is { } url
        && Uri.TryCreate(url.Replace('*', 'x').Replace('+', 'x'), UriKind.Absolute, out Uri? listening)
            ? listening.Port
            : DefaultPort;

    // In lower case, as Uri writes a host, so everything that names the server agrees on it.
    public string Url => $"https://{new UriBuilder(Uri.UriSchemeHttps, ServerNames.DnsName()).Uri.Host}:{Port}";
}
