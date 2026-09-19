using DDT.Server.Machines;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDT.Server.Deployments;

public static class DeploymentServiceCollectionExtensions
{
    // Reads both settings here rather than on first use: a mistake stops the server at startup, not the first
    // deployment hours later. That includes a misspelled key, which would otherwise leave a setting such as the
    // domain name unset without a word.
    public static IServiceCollection AddDdtDeployments(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        DeploymentOptions deployment;

        try
        {
            deployment = configuration
                .GetSection(DeploymentOptions.SectionName)
                .Get<DeploymentOptions>(binder => binder.ErrorOnUnknownConfiguration = true) ?? new DeploymentOptions();
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"{DeploymentOptions.SectionName} could not be read. Correct or remove the setting this names: {exception.Message}",
                exception);
        }

        DeploymentOptionsValidation.Validate(deployment);

        ZeroTouchNetworks zeroTouchNetworks = ZeroTouchNetworks.Parse(
            configuration.GetSection(MachineOptions.SectionName).Get<MachineOptions>()?.ZeroTouchNetworks);

        services.AddOptions<DeploymentOptions>().BindConfiguration(DeploymentOptions.SectionName);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(zeroTouchNetworks);
        services.AddSingleton<UnattendRenderer>();
        services.AddScoped<DeploymentService>();

        return services;
    }
}
