// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Tests;

// A host whose role manager cannot create the Viewer role from its first start on, and whose log is recorded.
public sealed class ViewerRefusingApplication : DdtApplication
{
    public RecordingLoggerProvider Log { get; } = new();

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureTestServices(services => services
            .AddSingleton<ILoggerProvider>(Log)
            .AddScoped<RoleManager<DdtRole>>(provider => new ViewerRefusingRoleManager(provider)));
    }
}
