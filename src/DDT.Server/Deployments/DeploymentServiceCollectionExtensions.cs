// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDT.Server.Deployments;

public static class DeploymentServiceCollectionExtensions
{
    // The deployment and machines settings are read from the snapshot where they are used, so none is read here.
    public static IServiceCollection AddDdtDeployments(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<UnattendRenderer>();
        services.AddScoped<DeploymentService>();
        services.AddScoped<RunReports>();
        services.AddScoped<RunSecrets>();

        // The passwords of stored accounts and of accounts given for one run, which reach only the steps of runs.
        services.AddSingleton<AccountProtector>();
        services.AddSingleton<RunCredentialProtector>();
        services.TryAddSingleton<IDomainDirectory, LdapDomainDirectory>();
        services.AddScoped<DomainJoinCheck>();
        services.AddSingleton<AbandonedRunSweeper>();
        services.AddHostedService(provider => provider.GetRequiredService<AbandonedRunSweeper>());
        services.AddSingleton<RunCredentialSweeper>();
        services.AddHostedService(provider => provider.GetRequiredService<RunCredentialSweeper>());

        return services;
    }
}
