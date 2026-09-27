// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Security.Cryptography;

namespace DDT.Server.Tests;

// Zips as an administrator uploads them for the console.
internal static class ConsolePackages
{
    // The console's three files, as Publish-Console.ps1 writes them, in folder when there is one.
    public static (string Path, byte[] Content)[] Files(string folder = "") =>
    [
        (folder + "ddt-console.exe", Executable(5000)),
        (folder + "libSkiaSharp.dll", Executable(3000)),
        (folder + "libHarfBuzzSharp.dll", Executable(2000)),
    ];

    public static byte[] Executable(int size) => [(byte)'M', (byte)'Z', .. RandomNumberGenerator.GetBytes(size)];

    public static byte[] Zip(params (string Path, byte[] Content)[] entries)
    {
        using MemoryStream zip = new();

        using (ZipArchive archive = new(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                entry.Write(content);
            }
        }

        return zip.ToArray();
    }

    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
}
