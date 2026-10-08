// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Tests;

// A server that can start both boot environments until a test takes one away. Only a test moves its clock.
public sealed class BothEnvironmentsApplication : DdtApplication
{
    // Both are there before the host is made, so a test that takes one away before its first request keeps it away.
    public BothEnvironmentsApplication()
    {
        SetLinuxBoot(true);
        SetBootImage(true);
    }

    public ManualTimeProvider Clock { get; } = new();

    public string LinuxBootFolder { get; } = Directory.CreateTempSubdirectory("ddt-linux-boot-").FullName;

    private string BootImagePath => Path.Combine(StorePath, "boot", "Boot", "boot.wim");

    // The five files a release brings and their description, or no description, which is no boot environment.
    public void SetLinuxBoot(bool exists)
    {
        if (exists)
        {
            TestLinuxBoot.Write(LinuxBootFolder);
        }
        else
        {
            TestLinuxBoot.Remove(LinuxBootFolder);
        }
    }

    // The server only looks whether the file is there.
    public void SetBootImage(bool exists) => Set(BootImagePath, exists ? "boot.wim" : null);

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("DDT:LinuxBoot:Path", LinuxBootFolder);

        // The test host serves no HTTPS, so it has no root of its own. Machines are handed this one.
        builder.ConfigureTestServices(services => services
            .AddSingleton<TimeProvider>(Clock)
            .AddSingleton(_ => TestLinuxBoot.Environment(LinuxBootFolder)));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // Disposed once for the using and once more by the factory
        if (disposing && Directory.Exists(LinuxBootFolder))
        {
            Directory.Delete(LinuxBootFolder, recursive: true);
        }
    }

    private static void Set(string path, string? content)
    {
        if (content is null)
        {
            File.Delete(path);

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
