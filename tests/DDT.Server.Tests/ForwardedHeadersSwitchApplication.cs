// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Hosting;

namespace DDT.Server.Tests;

// Turns on the framework's own switch, which ASPNETCORE_FORWARDEDHEADERS_ENABLED sets, with no proxy in DDT's settings.
public sealed class ForwardedHeadersSwitchApplication : DdtApplication
{
    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ForwardedHeaders_Enabled", "true");
    }
}
