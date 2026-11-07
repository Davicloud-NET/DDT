// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Certificates;
using DDT.Server.Machines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DDT.Server.LinuxBoot;

public static class LinuxBootServiceCollectionExtensions
{
    // The Linux boot environment a release brings, and what netboot asks about it and about each machine. The settings
    // and the deployments both need it, so calling this twice adds nothing twice.
    public static IServiceCollection AddDdtLinuxBoot(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<LinuxBootOptions>().BindConfiguration(LinuxBootOptions.SectionName);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new BundledReleases(AppContext.BaseDirectory));
        services.TryAddSingleton<ServerAddress>();

        // The root is DDT's own, which only a server that issues its certificate itself has.
        services.TryAddSingleton(provider => new LinuxBootEnvironment(
            provider.GetRequiredService<IOptions<LinuxBootOptions>>(),
            provider.GetRequiredService<BundledReleases>(),
            () => provider.GetService<ServerCertificates>()?.RootCertificatePem));
        services.TryAddSingleton<LinuxBootSite>();
        services.TryAddSingleton<BootChoices>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, BootChoiceRefresher>());

        return services;
    }
}
