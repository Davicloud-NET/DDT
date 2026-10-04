// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Hosting;

namespace DDT.Server.Tests;

// A host whose configuration names one more import folder, as a server next to an MDT deployment share has.
public sealed class ImageImportApplication : DdtApplication
{
    public string SharePath { get; } = Path.Combine(Path.GetTempPath(), "ddt-import-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("DDT:ImportFolders:0", SharePath);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(SharePath))
        {
            Directory.Delete(SharePath, recursive: true);
        }
    }
}
