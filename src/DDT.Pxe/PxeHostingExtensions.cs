// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDT.Pxe;

public static class PxeHostingExtensions
{
    public const string BootEndpointName = "Boot";

    public static PxeSetup AddDdtPxe(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        PxeOptions options = builder.Configuration
            .GetSection(PxeOptions.SectionName)
            .Get<PxeOptions>(binder => binder.ErrorOnUnknownConfiguration = true) ?? new PxeOptions();

        PxeSetup setup = PxeSetup.Create(options, NetworkInterfaceMap.FromHost(options.Interfaces));

        // Declared as configuration rather than a Listen call, because any explicit Listen makes Kestrel
        // ignore every endpoint configured elsewhere. Declared whether or not an interface is served, so
        // the set of listening ports does not depend on which NICs were up when the process started.
        builder.Configuration.AddInMemoryCollection(
        [
            new($"Kestrel:Endpoints:{BootEndpointName}:Url", string.Create(CultureInfo.InvariantCulture, $"http://0.0.0.0:{options.HttpBootPort}")),
            new($"Kestrel:Endpoints:{BootEndpointName}:Protocols", "Http1"),
        ]);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(setup);
        builder.Services.AddHostedService<PxeHost>();

        return setup;
    }

    public static void UseDdtBootListener(this WebApplication app, PxeSetup setup)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(setup);

        // With no interface served the gate answers 404 to everything on the boot port.
        app.UseBootListenerIsolation(setup.Options.HttpBootPort, setup.Interfaces);
        app.MapBootFiles(setup.Files);
    }
}
