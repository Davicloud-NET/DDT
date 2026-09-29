// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DDT.Pxe;

public static class PxeHostingExtensions
{
    public const string BootEndpointName = "Boot";

    // Only HttpBootPort and BootDirectory are read here, because they decide what Kestrel binds and what's served to
    // anyone. They stay in configuration. Everything else comes from the source, at each apply.
    public static PxeBootstrap AddDdtPxe(this WebApplicationBuilder builder, string storePath, Func<IServiceProvider, PxeHostSource> source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(source);

        PxeBootstrap bootstrap = ReadBootstrap(builder.Configuration, storePath);

        // This uses configuration, not a Listen call, because Listen makes Kestrel ignore every endpoint configured
        // elsewhere. It's declared even when nothing is served, so the listening ports never depend on which NICs were
        // up at startup.
        builder.Configuration.AddInMemoryCollection(
        [
            new($"Kestrel:Endpoints:{BootEndpointName}:Url", string.Create(CultureInfo.InvariantCulture, $"http://0.0.0.0:{bootstrap.HttpBootPort}")),
            new($"Kestrel:Endpoints:{BootEndpointName}:Protocols", "Http1"),
        ]);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(bootstrap);
        builder.Services.AddSingleton(provider => new PxeHost(
            source(provider),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILoggerFactory>()));
        builder.Services.AddHostedService(provider => provider.GetRequiredService<PxeHost>());

        return bootstrap;
    }

    // Reads and checks the configured values. A boot directory that contains the store would publish it.
    public static PxeBootstrap ReadBootstrap(IConfiguration configuration, string storePath)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        PxeOptions options = configuration.GetSection(PxeOptions.SectionName).Get<PxeOptions>() ?? new PxeOptions();

        SettingProblem.ThrowIfAny(PxeOptions.SectionName, PxeSetup.FindBootDirectoryProblems(options, storePath, configuration));

        return new PxeBootstrap(options.HttpBootPort, new BootFileResolver(PxeSetup.BootDirectoryIn(options.BootDirectory, storePath)));
    }

    public static void UseDdtBootListener(this WebApplication app, PxeBootstrap bootstrap)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(bootstrap);

        PxeHost host = app.Services.GetRequiredService<PxeHost>();

        // While no interface is served the gate answers 404 to everything on the boot port.
        app.UseBootListenerIsolation(bootstrap.HttpBootPort, () => host.Applied?.Interfaces);
        app.MapBootFiles(bootstrap.Files);
    }
}
