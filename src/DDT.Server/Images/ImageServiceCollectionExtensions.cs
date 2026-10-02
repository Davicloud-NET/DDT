// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.BootImage;
using DDT.Server.Import;
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
        services.AddSingleton<ImportFolders>();
        services.AddSingleton<CurrentImport>();
        services.AddSingleton<LibraryImports>();
        services.AddSingleton<BootImageCatalog>();
        services.TryAddSingleton<InstalledAdk>();
        services.TryAddSingleton<IBootImageHelper, PipeBootImageHelper>();
        services.AddSingleton<CurrentBootImageJob>();
        services.AddSingleton<BootImageViews>();
        services.AddSingleton<BootImagePushes>();
        services.AddSingleton<BootImageJobs>();
        services.AddSingleton<BuilderTokens>();
        services.AddSingleton<BuilderPackage>();
        services.AddSingleton<BootImageUploads>();
        services.AddHostedService<BootImageWatcher>();

        return services;
    }
}
