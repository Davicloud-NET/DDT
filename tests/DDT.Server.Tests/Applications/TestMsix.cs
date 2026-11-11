// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Text;

namespace DDT.Server.Tests.Applications;

// Zips that look like MSIX packages and bundles to the manifest reader: a manifest and a file of payload, no signature.
internal static class TestMsix
{
    private const string Publisher = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";

    public static byte[] Package(string name, bool framework = false) => Zip(
        ("AppxManifest.xml", $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="{name}" Publisher="{Publisher}" Version="1.2.3.0" ProcessorArchitecture="x64" />
              <Properties>
                <DisplayName>Contoso App</DisplayName>
                <PublisherDisplayName>Contoso</PublisherDisplayName>
                <Framework>{(framework ? "true" : "false")}</Framework>
              </Properties>
            </Package>
            """),
        ("App.exe", Guid.NewGuid().ToString()));

    public static byte[] Bundle(string name) => Zip(
        ("AppxMetadata/AppxBundleManifest.xml", $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle" SchemaVersion="4.0">
              <Identity Name="{name}" Publisher="{Publisher}" Version="2.0.0.0" />
            </Bundle>
            """),
        ("App_x64.msix", Guid.NewGuid().ToString()));

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using MemoryStream zip = new();

        using (ZipArchive archive = new(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                using Stream data = archive.CreateEntry(name).Open();
                data.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return zip.ToArray();
    }
}
