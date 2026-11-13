// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDT.Server.Applications;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddDdtApplications(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ApplicationViews>();
        services.AddScoped<ApplicationEditor>();
        services.AddScoped<ApplicationBundleEditor>();
        services.AddSingleton<ApplicationFilesSweeper>();
        services.AddHostedService(provider => provider.GetRequiredService<ApplicationFilesSweeper>());

        return services;
    }
}
