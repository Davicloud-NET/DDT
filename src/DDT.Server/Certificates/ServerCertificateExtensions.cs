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
            // renames one into place, while the selector already serves the new pair.
            kestrel.Configure(context.Configuration.GetSection("Kestrel"), reloadOnChange: false);
            kestrel.ConfigureHttpsDefaults(https => https.ServerCertificateSelector = (_, _) => certificates.Current);
        });

        return builder;
    }
}
