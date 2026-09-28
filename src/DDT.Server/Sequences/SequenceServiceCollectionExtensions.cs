// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Sequences;

public static class SequenceServiceCollectionExtensions
{
    public static IServiceCollection AddDdtSequences(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<SequenceCatalog>();
        services.AddScoped<SequenceResolver>();
        services.AddScoped<MachineValues>();

        return services;
    }
}
