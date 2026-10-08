// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DDT.Server.LinuxBoot;
using DDT.Server.Machines;
using Microsoft.Extensions.Options;

namespace DDT.Server.Tests;

// A Linux boot environment as a release brings it, small enough for a test: the five files and their description.
internal static class TestLinuxBoot
{
    // Stands in for the server's root, which the catalogue only hands on.
    public const string Root = "-----BEGIN CERTIFICATE-----\nMIIBtest\n-----END CERTIFICATE-----\n";

    public static readonly string[] Names = ["shimx64.efi", "grubx64.efi", "vmlinuz", "base.img", "ddt.img"];

    public static readonly string[] Keymaps = ["us", "uk", "de", "ch", "fr", "it", "es"];

    public static string ManifestPath(string folder) => Path.Combine(folder, LinuxBootEnvironment.ManifestName);

    // shim replaces the shim's content, such as a signed one. change edits the description before it is written.
    public static void Write(string folder, byte[]? shim = null, Action<JsonObject>? change = null)
    {
        Directory.CreateDirectory(folder);
        JsonArray files = [];

        foreach (string name in Names)
        {
            byte[] content = name == "shimx64.efi" && shim is not null ? shim : Encoding.ASCII.GetBytes("test " + name);
            Replace(Path.Combine(folder, name), content);
            files.Add(new JsonObject
            {
                ["name"] = name,
                ["size"] = content.Length,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(content)),
            });
        }

        JsonObject manifest = new()
        {
            ["schema"] = 1,
            ["snapshot"] = "20261006T203214Z",
            ["kernelRelease"] = "7.2.6+deb13-amd64",
            ["files"] = files,
            ["packages"] = new JsonArray(
                new JsonObject { ["name"] = "shim-signed", ["version"] = "1.51~1+deb13u1+16.1-2~deb13u1" },
                new JsonObject { ["name"] = "grub-efi-amd64-signed", ["version"] = "1+2.12+9+deb13u2" }),
            ["baseImage"] = new JsonObject { ["keymaps"] = new JsonArray([.. Keymaps.Select(keymap => (JsonNode)keymap)]) },
        };
        change?.Invoke(manifest);
        Retried(() => File.WriteAllText(ManifestPath(folder), manifest.ToJsonString()));
    }

    public static void Remove(string folder) => Retried(() => File.Delete(ManifestPath(folder)));

    // A file that already is what it should be is left alone, so tests that share a folder write it once.
    private static void Replace(string path, byte[] content)
    {
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
        {
            Retried(() => File.WriteAllBytes(path, content));
        }
    }

    // The catalogue checks the files in the background, and on Windows a file it still reads cannot be written.
    private static void Retried(Action write)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                write();

                return;
            }
            catch (IOException) when (attempt < 500)
            {
                Thread.Sleep(10);
            }
        }
    }

    // The catalogue of a folder, with a root to hand out unless a test takes it away.
    public static LinuxBootEnvironment Environment(string folder, Func<string?>? root = null) =>
        new(Options.Create(new LinuxBootOptions { Path = folder }), new BundledReleases(Path.Combine(folder, "none")), root ?? (() => Root));
}
