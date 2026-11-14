// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDT.Server.Records;

public static class RecordServiceCollectionExtensions
{
    // What runs hand the server: recovery keys and hardware hashes.
    public static IServiceCollection AddDdtRecords(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<RecoveryKeyProtector>();
        services.AddScoped<MachineRecordViews>();
        services.AddScoped<MachineRecordsPublisher>();
        services.AddScoped<RecoveryKeyRecords>();
        services.AddScoped<HardwareHashRecords>();
        services.AddScoped<RunRecords>();
        services.AddScoped<RecoveryKeyActions>();
        services.AddScoped<RecoveryKeySearch>();

        return services;
    }
}
