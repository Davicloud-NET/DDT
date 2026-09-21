// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Configuration;
using DDT.Server.Machines;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DDT.Server.Deployments;

public static class DeploymentServiceCollectionExtensions
{
    // Reads both settings here rather than on first use: a mistake stops the server at startup, not the first
    // deployment hours later. The instance checked here is the one every deployment uses, so no second binding can
    // differ from what was checked.
    public static IServiceCollection AddDdtDeployments(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        DeploymentOptions deployment = configuration
            .GetSection(DeploymentOptions.SectionName)
            .Get<DeploymentOptions>(binder => binder.ErrorOnUnknownConfiguration = true) ?? new DeploymentOptions();

        SettingProblem.ThrowIfAny(DeploymentOptions.SectionName, DeploymentOptionsValidation.FindProblems(deployment));

        ZeroTouchNetworks zeroTouchNetworks = ZeroTouchNetworks.Parse(
            configuration.GetSection(MachineOptions.SectionName).Get<MachineOptions>()?.ZeroTouchNetworks);

        services.AddSingleton(Options.Create(deployment));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(zeroTouchNetworks);
        services.AddSingleton<UnattendRenderer>();
        services.AddScoped<DeploymentService>();

        return services;
    }
}
