// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Text;

namespace DDT.Agent.Tests;

// Writes files that look like MSIX packages and bundles to the manifest reader: a manifest and no signature.
internal static class TestMsix
{
    public const string License = """<License Version="1" xmlns="urn:schemas-microsoft-com:windows:store:licensing:ls"/>""";

    private const string Publisher = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";

    public static void WritePackage(string path, string name, bool framework = false) => Write(
        path,
        "AppxManifest.xml",
        $"""<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Identity Name="{name}" Publisher="{Publisher}" Version="1.0.0.0" /><Properties><Framework>{(framework ? "true" : "false")}</Framework></Properties></Package>""");

    public static void WriteBundle(string path, string name) => Write(
        path,
        "AppxMetadata/AppxBundleManifest.xml",
        $"""<Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle"><Identity Name="{name}" Publisher="{Publisher}" Version="1.0.0.0" /></Bundle>""");

    public static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, text);
    }

    private static void Write(string path, string manifestName, string manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        using FileStream file = new(path, FileMode.Create);
        using ZipArchive archive = new(file, ZipArchiveMode.Create);
        using Stream data = archive.CreateEntry(manifestName).Open();
        data.Write(Encoding.UTF8.GetBytes(manifest));
    }
}
