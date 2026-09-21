// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Hosting;

namespace DDT.Server.Tests;

public sealed class WaitingCapApplication : DdtApplication
{
    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("DDT:Machines:MaxWaitingPerAddress", "2");
        builder.UseSetting("DDT:Machines:MaxWaiting", "3");
    }
}
