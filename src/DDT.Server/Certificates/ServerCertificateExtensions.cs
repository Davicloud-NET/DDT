// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDT.Server.Certificates;

public static class ServerCertificateExtensions
{
    // Every HTTPS endpoint serves the certificate held in memory, whatever Kestrel:Certificates:Default loaded at startup.
    public static WebApplicationBuilder AddDdtServerCertificates(this WebApplicationBuilder builder, ServerCertificates certificates)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(certificates);

        builder.Services.AddSingleton(certificates);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddHostedService<ServerCertificateRenewer>();

        builder.WebHost.ConfigureKestrel((context, kestrel) =>
        {
            // Kestrel would otherwise watch the certificate files and load its whole configuration again whenever a renewal
            // renames one into place, while DDT already serves the new pair from memory.
            kestrel.Configure(context.Configuration.GetSection("Kestrel"), reloadOnChange: false);
            kestrel.ConfigureHttpsDefaults(https =>
            {
                // The selector keeps Kestrel from loading Kestrel:Certificates:Default itself, but a selected
                // certificate goes out without the intermediates of an administrator's certificate. So each
                // connection is handed the context instead, which carries them.
                https.ServerCertificateSelector = (_, _) => certificates.Current;
                https.OnAuthenticate = (_, tls) =>
                {
                    tls.ServerCertificateSelectionCallback = null;
                    tls.ServerCertificateContext = certificates.Context;
                };
            });
        });

        return builder;
    }
}
