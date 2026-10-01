// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Machines;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Tests;

// A server that came with an agent and a console, as a release puts them next to DDT.Host.
public sealed class BundledReleaseApplication : DdtApplication
{
    public BundledReleases Bundled { get; } = new(Directory.CreateTempSubdirectory("ddt-bundled-").FullName);

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureTestServices(services => services.AddSingleton(Bundled));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // Disposed once for the using and once more by the factory
        if (disposing && Directory.Exists(Bundled.Folder))
        {
            Directory.Delete(Bundled.Folder, recursive: true);
        }
    }
}
