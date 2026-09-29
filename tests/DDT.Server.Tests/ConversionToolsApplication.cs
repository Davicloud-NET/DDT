// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using DDT.Server.Images;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Tests;

// A host that only has the conversion tools a test puts into Tools, by name.
public sealed class ConversionToolsApplication : DdtApplication
{
    public ConcurrentDictionary<string, string> Tools { get; } = new(StringComparer.Ordinal);

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureTestServices(services =>
            services.AddSingleton(new ConversionTools(tool => Tools.TryGetValue(tool, out string? path) ? path : null)));
    }
}
