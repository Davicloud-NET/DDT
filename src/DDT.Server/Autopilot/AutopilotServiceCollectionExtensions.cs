// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDT.Server.Autopilot;

public static class AutopilotServiceCollectionExtensions
{
    // The client for Microsoft Entra ID's token endpoint and Microsoft Graph.
    public const string GraphClient = "autopilot";

    // Autopilot uploads through Microsoft Graph, the CSV export and the machine page's upload action.
    public static IServiceCollection AddDdtAutopilot(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(GraphClient, client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<GraphTokens>();
        services.TryAddSingleton<IAutopilotDirectory, GraphAutopilotDirectory>();
        services.AddScoped<AutopilotActions>();
        services.AddSingleton<AutopilotUploader>();
        services.AddHostedService<AutopilotUploadLoop>();

        return services;
    }
}
