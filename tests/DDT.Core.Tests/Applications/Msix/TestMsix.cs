// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Security;
using System.Text;

namespace DDT.Core.Tests.Applications.Msix;

// Zips that look like MSIX packages and bundles to the manifest reader: a manifest and a file of payload, no signature.
internal static class TestMsix
{
    public const string Microsoft = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";
    public const string License = """<?xml version="1.0" encoding="utf-8"?><License Version="1" xmlns="urn:schemas-microsoft-com:windows:store:licensing:ls"><Binding/></License>""";

    public static string Manifest(string name, string displayName = "Contoso App", bool framework = false, string version = "1.2.3.0") =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="{name}" Publisher="{SecurityElement.Escape(Microsoft)}" Version="{version}" ProcessorArchitecture="x64" />
          <Properties>
            <DisplayName>{displayName}</DisplayName>
            <PublisherDisplayName>Contoso</PublisherDisplayName>
            <Framework>{(framework ? "true" : "false")}</Framework>
          </Properties>
        </Package>
        """;

    public static string BundleManifest(string name) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle" SchemaVersion="4.0">
          <Identity Name="{name}" Publisher="{SecurityElement.Escape(Microsoft)}" Version="2.0.0.0" />
          <Packages />
        </Bundle>
        """;

    public static byte[] Package(string name, bool framework = false) => Zip(("AppxManifest.xml", Manifest(name, framework: framework)), ("App.exe", "MZ"));

    public static byte[] Bundle(string name) => Zip(("AppxMetadata/AppxBundleManifest.xml", BundleManifest(name)), ($"{name}_x64.msix", "payload"));

    public static byte[] Zip(params (string Name, string Content)[] entries) =>
        Zip(CompressionLevel.Optimal, [.. entries.Select(entry => (entry.Name, Encoding.UTF8.GetBytes(entry.Content)))]);

    public static byte[] Zip(CompressionLevel level, params (string Name, byte[] Content)[] entries)
    {
        using MemoryStream zip = new();

        using (ZipArchive archive = new(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in entries)
            {
                using Stream data = archive.CreateEntry(name, level).Open();
                data.Write(content);
            }
        }

        return zip.ToArray();
    }
}
