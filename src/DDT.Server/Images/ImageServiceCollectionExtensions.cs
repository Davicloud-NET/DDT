// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.BootImage;
using DDT.Server.Packages;
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
        services.AddSingleton<UploadRefusals>();
        services.AddSingleton<WimUploadCommitter>();
        services.AddSingleton<RawUploadCommitter>();
        services.AddSingleton<PackageUploadCommitter>();
        services.AddSingleton<UploadCommitter>();
        services.AddSingleton<ImageUploadCompleter>();
        services.TryAddSingleton<ConversionTools>();
        services.AddSingleton<RawImageImporter>();
        services.AddScoped<ImageUploadSessions>();
        services.AddScoped<ImageLibrary>();
        services.AddScoped<PackageLibrary>();
        services.AddSingleton<ImageUploadSweeper>();
        services.AddHostedService(provider => provider.GetRequiredService<ImageUploadSweeper>());
        services.AddSingleton<BootImageCatalog>();
        services.AddHostedService<BootImageWatcher>();

        return services;
    }
}
