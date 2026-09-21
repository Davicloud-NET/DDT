// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDT.Server.Images;

public static class ImageServiceCollectionExtensions
{
    public static IServiceCollection AddDdtImages(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ImageStore>();
        services.AddSingleton<ImageUploadLocks>();
        services.AddSingleton<ImageUploadCompleter>();
        services.AddScoped<ImageUploadSessions>();
        services.AddSingleton<ImageUploadSweeper>();
        services.AddHostedService(provider => provider.GetRequiredService<ImageUploadSweeper>());

        return services;
    }
}
