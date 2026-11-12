// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security;
using System.Text;
using DDT.Core.Applications.Msix;
using Xunit;

namespace DDT.Core.Tests.Applications.Msix;

public sealed class MsixManifestReaderTests
{
    private static MsixManifest? Read(byte[] package)
    {
        using MemoryStream data = new(package);

        return MsixManifestReader.Read(data);
    }

    [Fact]
    public void ReadsAPackagesIdentityAndProperties()
    {
        MsixManifest? manifest = Read(TestMsix.Package("Contoso.App"));

        Assert.Equal(
            new MsixManifest("Contoso.App", TestMsix.Microsoft, "1.2.3.0", "x64", "Contoso App", "Contoso", IsFramework: false, IsBundle: false),
            manifest);
        Assert.Equal("Contoso.App_8wekyb3d8bbwe", manifest?.FamilyName);
    }

    [Fact]
    public void TellsAFrameworkPackage() =>
        Assert.True(Read(TestMsix.Package("Microsoft.VCLibs.140.00", framework: true))?.IsFramework);

    [Fact]
    public void ReadsABundleWhichIsNeverAFramework()
    {
        MsixManifest? bundle = Read(TestMsix.Bundle("Contoso.App"));

        Assert.Equal(new MsixManifest("Contoso.App", TestMsix.Microsoft, "2.0.0.0", null, "Contoso.App", "Microsoft Corporation", false, true), bundle);
    }

    [Theory]
    [InlineData("ms-resource:AppName")]
    [InlineData("MS-RESOURCE:///Resources/AppName")]
    [InlineData(" ")]
    public void ADisplayNameFromTheResourcesFallsBackToTheIdentityName(string displayName) =>
        Assert.Equal("Contoso.App", Read(TestMsix.Zip(("AppxManifest.xml", TestMsix.Manifest("Contoso.App", displayName))))?.DisplayName);

    // Microsoft.PowerShell's bundle names nothing itself, its x64 package says PowerShell and Microsoft Corporation.
    [Fact]
    public void ABundleIsNamedByItsFirstApplicationPackage()
    {
        string bundleManifest = TestMsix.BundleManifest("Contoso.App").Replace(
            "<Packages />",
            """<Packages><Package Type="resource" FileName="Contoso.App_de.msix" /><Package Type="application" FileName="Contoso.App_x64.msix" /></Packages>""",
            StringComparison.Ordinal);
        byte[] bundle = TestMsix.Zip(
            CompressionLevel.NoCompression,
            ("AppxMetadata/AppxBundleManifest.xml", Encoding.UTF8.GetBytes(bundleManifest)),
            ("Contoso.App_de.msix", TestMsix.Zip(("AppxManifest.xml", TestMsix.Manifest("Contoso.App", "Ressourcen")))),
            ("Contoso.App_x64.msix", TestMsix.Package("Contoso.App")));

        MsixManifest? read = Read(bundle);

        Assert.Equal(("Contoso App", "Contoso", "2.0.0.0", true), (read?.DisplayName, read?.PublisherDisplayName, read?.Version, read?.IsBundle));
    }

    [Fact]
    public void ABundleWhosePackageCannotBeReadKeepsItsIdentitysNames()
    {
        string bundleManifest = TestMsix.BundleManifest("Contoso.App").Replace(
            "<Packages />",
            """<Packages><Package Type="application" FileName="Contoso.App_x64.msix" /></Packages>""",
            StringComparison.Ordinal);

        MsixManifest? read = Read(TestMsix.Zip(("AppxMetadata/AppxBundleManifest.xml", bundleManifest), ("Contoso.App_x64.msix", "no zip")));

        Assert.Equal(("Contoso.App", "Microsoft Corporation"), (read?.DisplayName, read?.PublisherDisplayName));
    }

    [Theory]
    [InlineData("ms-resource:PublisherName", "CN=Microsoft Corporation, O=Microsoft Corporation, C=US", "Microsoft Corporation")]
    [InlineData("ms-resource:PublisherName", "O=Contoso, CN=\"Contoso, Ltd.\", C=AT", "Contoso, Ltd.")]
    [InlineData("ms-resource:PublisherName", "O=Contoso, C=AT", null)]
    public void APublisherFromTheResourcesFallsBackToTheCommonName(string shown, string publisher, string? expected)
    {
        string manifest = TestMsix.Manifest("Contoso.App")
            .Replace("<PublisherDisplayName>Contoso</PublisherDisplayName>", $"<PublisherDisplayName>{shown}</PublisherDisplayName>", StringComparison.Ordinal)
            .Replace(SecurityElement.Escape(TestMsix.Microsoft), SecurityElement.Escape(publisher), StringComparison.Ordinal);

        Assert.Equal(expected, Read(TestMsix.Zip(("AppxManifest.xml", manifest)))?.PublisherDisplayName);
    }

    [Fact]
    public void ReadsTheOlderAppxManifestToo()
    {
        string manifest = TestMsix.Manifest("Contoso.Old").Replace(
            "http://schemas.microsoft.com/appx/manifest/foundation/windows10",
            "http://schemas.microsoft.com/appx/2010/manifest",
            StringComparison.Ordinal);

        Assert.Equal("Contoso.Old", Read(TestMsix.Zip(("AppxManifest.xml", manifest)))?.Name);
    }

    public static TheoryData<string, byte[]> Unreadable => new()
    {
        { "no zip", "MZ not a zip"u8.ToArray() },
        { "no manifest", TestMsix.Zip(("App.exe", "MZ")) },
        { "manifest in a folder", TestMsix.Zip(("Contoso/AppxManifest.xml", TestMsix.Manifest("Contoso.App"))) },
        { "no XML", TestMsix.Zip(("AppxManifest.xml", "<Package><Identity")) },
        { "another root", TestMsix.Zip(("AppxManifest.xml", TestMsix.BundleManifest("Contoso.App"))) },
        { "a name with a space", TestMsix.Zip(("AppxManifest.xml", TestMsix.Manifest("Contoso App"))) },
        { "a name with an underscore", TestMsix.Zip(("AppxManifest.xml", TestMsix.Manifest("Contoso_App"))) },
        { "a version of three parts", TestMsix.Zip(("AppxManifest.xml", TestMsix.Manifest("Contoso.App", version: "1.2.3"))) },
        { "a version over 65535", TestMsix.Zip(("AppxManifest.xml", TestMsix.Manifest("Contoso.App", version: "1.2.3.65536"))) },
    };

    [Theory]
    [MemberData(nameof(Unreadable))]
    public void WhatIsNoPackageIsUnreadable(string what, byte[] package)
    {
        Assert.NotNull(what);
        Assert.Null(Read(package));
    }

    [Fact]
    public void ADocumentTypeIsRefusedSoNoEntityCanGrowTheManifest()
    {
        string manifest = TestMsix.Manifest("Contoso.App", "&big;").Replace(
            "<Package ",
            """<!DOCTYPE Package [<!ENTITY big "Contoso">]><Package """,
            StringComparison.Ordinal);

        Assert.Null(Read(TestMsix.Zip(("AppxManifest.xml", manifest))));
    }

    [Fact]
    public void AManifestOverFourMegabytesIsNotRead()
    {
        string manifest = TestMsix.Manifest("Contoso.App", new string('a', 4 * 1024 * 1024));

        Assert.Null(Read(TestMsix.Zip(("AppxManifest.xml", manifest))));
    }

    [Fact]
    public void AManifestThatSaysItIsSmallButInflatesFurtherIsCutOff()
    {
        byte[] package = TestMsix.Zip(("AppxManifest.xml", TestMsix.Manifest("Contoso.App", new string('a', 5 * 1024 * 1024))));
        PatchDeclaredSizes(package, 1000);

        Assert.Null(Read(package));
    }

    [Fact]
    public void AZipThatClaimsMoreEntriesThanItCanHoldIsUnreadable()
    {
        byte[] package = TestMsix.Package("Contoso.App");

        // 0xFFFF entries means the count is in a ZIP64 record, which this zip does not have.
        BinaryPrimitives.WriteUInt16LittleEndian(package.AsSpan(package.Length - 12), ushort.MaxValue);

        Assert.Null(Read(package));
    }

    // Writes size into the central directory's uncompressed size of the first entry.
    private static void PatchDeclaredSizes(byte[] zip, uint size)
    {
        int end = zip.Length - 22;
        int central = (int)BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(end + 16));
        BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(central + 24), size);

        using MemoryStream data = new(zip);
        using ZipArchive archive = new(data, ZipArchiveMode.Read);
        Assert.Equal(size, archive.Entries[0].Length);
    }
}
