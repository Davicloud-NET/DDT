using DDT.Contracts;
using DDT.Server.Live;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDT.Server.Machines;

public static class MachineServiceCollectionExtensions
{
    public static IServiceCollection AddDdtMachines(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<MachineOptions>().BindConfiguration(MachineOptions.SectionName);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<WaitingMachineSweeper>();
        services.AddHostedService(provider => provider.GetRequiredService<WaitingMachineSweeper>());
        services.AddScoped<MachineRegistrar>();
        services.AddSingleton<LiveNotifier>();

        services.AddSignalR()
            .AddJsonProtocol(json => json.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, DdtJsonContext.Default));

        return services;
    }
}
