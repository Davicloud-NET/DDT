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
