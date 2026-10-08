// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.E2E;

// What makes the host take both boot environments for there, from the moment this is made until it is disposed.
internal sealed class BootEnvironmentFiles : IDisposable
{
    private readonly string _bootImage;
    private readonly string _linuxBootDescription;

    public BootEnvironmentFiles(string storePath, string linuxBootFolder)
    {
        _bootImage = Path.Combine(storePath, "boot", "Boot", "boot.wim");
        _linuxBootDescription = Path.Combine(linuxBootFolder, "ddt-linux-boot.json");

        // No machine netboots here, so the host only ever looks whether the files are there.
        Write(_bootImage, "Stands in for a boot image.");
        Write(_linuxBootDescription, """{"snapshot":"20261006T203214Z","kernelRelease":"7.2.6+deb13-amd64","files":{}}""");
    }

    public void Dispose()
    {
        File.Delete(_bootImage);
        File.Delete(_linuxBootDescription);
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new ArgumentException($"{path} has no folder.", nameof(path)));
        File.WriteAllText(path, content);
    }
}
