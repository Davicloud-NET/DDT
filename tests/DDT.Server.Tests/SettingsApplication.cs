// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Hosting;

namespace DDT.Server.Tests;

// A host with extra settings, for a test that needs its own configuration.
public class SettingsApplication(params (string Key, string Value)[] settings) : DdtApplication
{
    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        foreach ((string key, string value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
