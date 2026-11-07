// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace DDT.E2E;

// What makes the host take both boot environments for there, from the moment this is made until it is disposed.
internal sealed class BootEnvironmentFiles : IDisposable
{
    private static readonly string[] s_linuxFiles = ["shimx64.efi", "grubx64.efi", "vmlinuz", "base.img", "ddt.img"];

    private readonly string _bootImage;
    private readonly string _linuxBootDescription;

    public BootEnvironmentFiles(string storePath, string linuxBootFolder)
    {
        _bootImage = Path.Combine(storePath, "boot", "Boot", "boot.wim");
        _linuxBootDescription = Path.Combine(linuxBootFolder, "ddt-linux-boot.json");

        // No machine netboots here, so the host only ever looks whether the files are there and whole.
        Write(_bootImage, "Stands in for a boot image.");
        Write(_linuxBootDescription, Description(linuxBootFolder));
    }

    // The five files a release brings, each a line of text here, and the description that lists them with their hashes.
    private static string Description(string folder)
    {
        JsonArray files = [];

        foreach (string name in s_linuxFiles)
        {
            byte[] content = Encoding.ASCII.GetBytes("Stands in for " + name);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name), content);
            files.Add(new JsonObject { ["name"] = name, ["size"] = content.Length, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(content)) });
        }

        return new JsonObject { ["schema"] = 1, ["snapshot"] = "20261006T203214Z", ["kernelRelease"] = "7.2.6+deb13-amd64", ["files"] = files }.ToJsonString();
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
