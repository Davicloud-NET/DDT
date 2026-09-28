// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts;
using DDT.Server.Live;
using DDT.Server.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDT.Server.Machines;

public static class MachineServiceCollectionExtensions
{
    public static IServiceCollection AddDdtMachines(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<AgentReleaseOptions>().BindConfiguration(AgentReleaseOptions.SectionName);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AgentReleaseStore>();
        services.AddSingleton<ConsoleReleaseStore>();
        services.AddSingleton<ConsoleLogoStore>();
        services.AddSingleton<WaitingMachineSweeper>();
        services.AddHostedService(provider => provider.GetRequiredService<WaitingMachineSweeper>());
        services.AddScoped<MachineArrivals>();
        services.AddScoped<RegistrationPublisher>();
        services.AddScoped<MachineRegistrar>();
        services.AddScoped<MachinePolls>();
        services.AddScoped<MachineSignIn>();
        services.AddScoped<SignInApprovals>();
        services.AddScoped<MachineLogs>();
        services.AddScoped<MachineTransitions>();
        services.AddScoped<MachineApprovals>();
        services.AddScoped<MachineRemovals>();
        services.AddSingleton<LiveNotifier>();

        // The sweeper and the registrar change the machines the rules count.
        services.AddSingleton<RuleRecount>();
        services.AddSingleton<LiveConnections>();

        // The context's own option reaches only its own options, not these.
        services.AddSignalR()
            .AddJsonProtocol(json =>
            {
                json.PayloadSerializerOptions.AllowOutOfOrderMetadataProperties = true;
                json.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, DdtJsonContext.Default);
            });

        return services;
    }
}
