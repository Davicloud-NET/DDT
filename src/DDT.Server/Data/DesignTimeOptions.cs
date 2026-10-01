// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Data;

// dotnet ef makes a context without the host. Without UseApplicationServiceProvider the design time model falls back
// to Identity schema version 1, which silently drops the passkey table from generated migrations.
internal static class DesignTimeOptions
{
    public static DbContextOptionsBuilder<TContext> For<TContext>()
        where TContext : DbContext
    {
        ServiceCollection services = new();
        services.AddOptions<IdentityOptions>()
            .Configure(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3);

        return new DbContextOptionsBuilder<TContext>().UseApplicationServiceProvider(services.BuildServiceProvider());
    }

    // Writing a migration opens no connection, so the fallback only has to parse
    public static string Connection(string fallback) =>
        Environment.GetEnvironmentVariable("DDT_DESIGN_TIME_CONNECTION") ?? fallback;
}
