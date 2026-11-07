// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;

namespace DDT.Pxe.Tests;

// A Linux boot environment as the server hands it to netboot: a folder and its listed files as they are on disk now.
internal static class TestBundle
{
    public static LinuxBootBundle Of(string folder, params string[] names) =>
        new(folder, Stamps(folder, names), new Uri("https://ddt-server.example.test:8443"), "root");

    public static IReadOnlyDictionary<string, BootFileStamp> Stamps(string folder, params string[] names) =>
        names.ToFrozenDictionary(name => name, name => Stamp(Path.Combine(folder, name)), StringComparer.Ordinal);

    public static BootFileStamp Stamp(string path)
    {
        FileInfo file = new(path);

        return new BootFileStamp(file.Length, file.LastWriteTimeUtc);
    }
}
