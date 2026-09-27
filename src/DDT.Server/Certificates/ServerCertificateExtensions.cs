// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDT.Server.Certificates;

public static class ServerCertificateExtensions
{
    // The connection item that names the pair a connection was served, by thumbprint.
    private const string ServedKey = "ddt.served-certificate";

    // Every HTTPS endpoint serves the certificate held in memory, whatever Kestrel:Certificates:Default loaded at startup.
    public static WebApplicationBuilder AddDdtServerCertificates(this WebApplicationBuilder builder, ServerCertificates certificates)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(certificates);

        builder.Services.AddSingleton(certificates);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddHostedService<ServerCertificateRenewer>();
        builder.Services.AddSingleton<IStartupFilter>(new StaleConnectionFilter(certificates));

        builder.WebHost.ConfigureKestrel((context, kestrel) =>
        {
            // Kestrel would otherwise watch the certificate files and load its whole configuration again whenever a renewal
            // renames one into place, while DDT already serves the new pair from memory.
            kestrel.Configure(context.Configuration.GetSection("Kestrel"), reloadOnChange: false);
            kestrel.ConfigureHttpsDefaults(https =>
            {
                // The selector keeps Kestrel from loading Kestrel:Certificates:Default itself, but a selected
                // certificate goes out without the intermediates of an administrator's certificate. So each
                // connection is handed the context instead, which carries them, and remembers which pair it got.
                https.ServerCertificateSelector = (_, _) => certificates.Current;
                https.OnAuthenticate = (connection, tls) =>
                {
                    SslStreamCertificateContext? served = certificates.Context;
                    tls.ServerCertificateSelectionCallback = null;
                    tls.ServerCertificateContext = served;
                    connection.Items[ServedKey] = served?.TargetCertificate.Thumbprint;
                };
            });
        });

        return builder;
    }

    // The thumbprint of the pair the request's connection was served, null without TLS.
    public static string? ServedThumbprint(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Features.Get<IConnectionItemsFeature>()?.Items.TryGetValue(ServedKey, out object? served) == true
            ? served as string
            : null;
    }

    // While a provisional pair waits for its confirmation, a connection that was served another pair is closed after its
    // answer, so the browser's next request makes a new one, gets the new pair, and can confirm it.
    private sealed class StaleConnectionFilter(ServerCertificates certificates) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (certificates.Provisional is { } provisional
                    && ServedThumbprint(context) is { } served
                    && !string.Equals(served, provisional.Thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    context.Features.Get<IConnectionLifetimeNotificationFeature>()?.RequestClose();
                }

                return nextMiddleware(context);
            });

            next(app);
        };
    }
}
