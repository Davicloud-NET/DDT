// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Tests;

// A server that can start both boot environments: a boot image is built, and configuration names a folder with the
// Linux boot environment. A test takes either away again. The clock moves only when a test advances it.
public sealed class BothEnvironmentsApplication : DdtApplication
{
    private const string Manifest = """{"snapshot":"20261006T203214Z","kernelRelease":"7.2.6+deb13-amd64","files":{}}""";

    public ManualTimeProvider Clock { get; } = new();

    public string LinuxBootFolder { get; } = Directory.CreateTempSubdirectory("ddt-linux-boot-").FullName;

    private string ManifestPath => Path.Combine(LinuxBootFolder, "ddt-linux-boot.json");

    private string BootImagePath => Path.Combine(StorePath, "boot", "Boot", "boot.wim");

    public void SetLinuxBoot(bool exists) => Set(ManifestPath, exists ? Manifest : null);

    // The server only looks whether the file is there.
    public void SetBootImage(bool exists) => Set(BootImagePath, exists ? "boot.wim" : null);

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        SetLinuxBoot(true);
        SetBootImage(true);
        builder.UseSetting("DDT:LinuxBoot:Path", LinuxBootFolder);
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
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
