using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Images;

public static class ImageServiceCollectionExtensions
{
    public static IServiceCollection AddDdtImages(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ImageStore>();

        return services;
    }
}
