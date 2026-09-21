// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Tests;

// A host with extra settings whose log is recorded.
public sealed class LoggedApplication(params (string Key, string Value)[] settings) : SettingsApplication(settings)
{
    public RecordingLoggerProvider Log { get; } = new();

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        base.ConfigureTestHost(builder);

        builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(Log));
    }
}
