// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Core.Wim;
using Xunit;

namespace DDT.Core.Tests.Wim;

public sealed class WimMetadataTests
{
    private const string Professional = """
        <IMAGE INDEX="1">
          <TOTALBYTES>22954544606</TOTALBYTES>
          <HARDLINKBYTES>9960000000</HARDLINKBYTES>
          <WINDOWS>
            <ARCH>9</ARCH>
            <PRODUCTNAME>Microsoft® Windows® Operating System</PRODUCTNAME>
            <EDITIONID>Professional</EDITIONID>
            <INSTALLATIONTYPE>Client</INSTALLATIONTYPE>
            <LANGUAGES>
              <LANGUAGE>de-DE</LANGUAGE>
              <DEFAULT>de-DE</DEFAULT>
            </LANGUAGES>
            <VERSION>
              <MAJOR>10</MAJOR>
              <MINOR>0</MINOR>
              <BUILD>26100</BUILD>
              <SPBUILD>1742</SPBUILD>
            </VERSION>
          </WINDOWS>
          <NAME>Windows 11 Pro</NAME>
          <DESCRIPTION>Windows 11 Pro &amp; more</DESCRIPTION>
          <FLAGS>ProfessionalFlag</FLAGS>
        </IMAGE>
        """;

    private const string Setup = """
        <IMAGE INDEX="2">
          <TOTALBYTES>1500</TOTALBYTES>
          <HARDLINKBYTES>0</HARDLINKBYTES>
          <WINDOWS><ARCH>12</ARCH></WINDOWS>
          <NAME>Setup</NAME>
          <FLAGS>WindowsPE</FLAGS>
        </IMAGE>
        """;

    private static async Task<IReadOnlyList<WimImageInfo>> ReadAsync(SyntheticWim wim)
    {
        using MemoryStream stream = wim.ToStream();

        return await WimMetadata.ReadAsync(stream, TestContext.Current.CancellationToken);
    }

    private static async Task<WimImageInfo> ReadImageAsync(string image) =>
        Assert.Single(await ReadAsync(SyntheticWim.WithImages(image)));

    private static async Task<string> RefusalAsync(SyntheticWim wim) =>
        (await Assert.ThrowsAsync<InvalidWimException>(() => ReadAsync(wim))).Message;

    [Fact]
    public async Task ReadsEveryImageInIndexOrder()
    {
        IReadOnlyList<WimImageInfo> images = await ReadAsync(SyntheticWim.WithImages(Setup, Professional));

        Assert.Equal(
            [
                new WimImageInfo(1, "Windows 11 Pro", "Windows 11 Pro & more", "Professional", "x64", "10.0.26100.1742", "de-DE", 22954544606, 9960000000),
                new WimImageInfo(2, "Setup", null, "WindowsPE", "arm64", null, null, 1500, 0),
            ],
            images);
    }

    [Fact]
    public async Task InstalledBytesLeaveOutHardLinkedData()
    {
        WimImageInfo image = await ReadImageAsync(Professional);

        Assert.Equal(22954544606 - 9960000000, image.InstalledBytes);
    }

    [Theory]
    [InlineData("0", "x86")]
    [InlineData("9", "x64")]
    [InlineData("12", "arm64")]
    [InlineData("5", null)]
    [InlineData("6", null)]
    [InlineData("", null)]
    [InlineData("x64", null)]
    public async Task MapsTheArchitecture(string arch, string? expected)
    {
        WimImageInfo image = await ReadImageAsync($"<IMAGE INDEX=\"1\"><WINDOWS><ARCH>{arch}</ARCH></WINDOWS></IMAGE>");

        Assert.Equal(expected, image.Architecture);
    }

    [Fact]
    public async Task FallsBackToTheFlagsWithoutAnEditionId()
    {
        WimImageInfo image = await ReadImageAsync("<IMAGE INDEX=\"1\"><WINDOWS><ARCH>9</ARCH></WINDOWS><FLAGS>EnterpriseEval</FLAGS></IMAGE>");

        Assert.Equal("EnterpriseEval", image.EditionId);
    }

    [Theory]
    [InlineData("<MAJOR>10</MAJOR><MINOR>0</MINOR><BUILD>26200</BUILD><SPBUILD>6584</SPBUILD>", "10.0.26200.6584")]
    [InlineData("<MAJOR>6</MAJOR><MINOR>1</MINOR><BUILD>7601</BUILD>", "6.1.7601")]
    [InlineData("<MAJOR>10</MAJOR><MINOR>0</MINOR><SPBUILD>1</SPBUILD>", null)]
    [InlineData("<MAJOR>ten</MAJOR><MINOR>0</MINOR><BUILD>1</BUILD>", null)]
    [InlineData("", null)]
    public async Task FormatsTheVersion(string parts, string? expected)
    {
        WimImageInfo image = await ReadImageAsync($"<IMAGE INDEX=\"1\"><WINDOWS><VERSION>{parts}</VERSION></WINDOWS></IMAGE>");

        Assert.Equal(expected, image.Version);
    }

    [Fact]
    public async Task LeavesOutWhatTheImageDoesNotDescribe()
    {
        WimImageInfo image = await ReadImageAsync("<IMAGE INDEX=\"1\"><NAME> </NAME><TOTALBYTES>-5</TOTALBYTES></IMAGE>");

        Assert.Equal(new WimImageInfo(1, "", null, null, null, null, null, 0, 0), image);
    }

    [Fact]
    public async Task ReadsTheHeaderFromTheStartWhereverTheStreamIs()
    {
        using MemoryStream stream = new SyntheticWim().ToStream();
        stream.Position = 100;

        IReadOnlyList<WimImageInfo> images = await WimMetadata.ReadAsync(stream, TestContext.Current.CancellationToken);

        Assert.Equal("Image", Assert.Single(images).Name);
    }

    [Fact]
    public async Task ReadsASolidWim()
    {
        IReadOnlyList<WimImageInfo> images = await ReadAsync(new SyntheticWim { Version = 0xE00 });

        Assert.Equal("Image", Assert.Single(images).Name);
    }

    [Fact]
    public async Task RefusesAFileWithoutTheWimMagic()
    {
        Assert.Equal("This file is not a WIM image.", await RefusalAsync(new SyntheticWim { Magic = "MSWIN\0\0\0" }));
    }

    [Fact]
    public async Task RefusesAFileShorterThanTheHeader()
    {
        using MemoryStream stream = new(Encoding.ASCII.GetBytes("MSWIM\0\0\0 definitely not a wim"));

        InvalidWimException refusal = await Assert.ThrowsAsync<InvalidWimException>(
            () => WimMetadata.ReadAsync(stream, TestContext.Current.CancellationToken));

        Assert.Equal("This file is not a WIM image.", refusal.Message);
    }

    [Fact]
    public async Task RefusesAHeaderOfAnotherLength()
    {
        Assert.Equal("This file is not a WIM image.", await RefusalAsync(new SyntheticWim { HeaderLengthField = 200 }));
    }

    [Fact]
    public async Task RefusesAPipableWim()
    {
        Assert.Equal(
            "Pipable WIM files are not supported. Export the image into a regular WIM first.",
            await RefusalAsync(new SyntheticWim { Magic = "WLPWM\0\0\0" }));
    }

    [Fact]
    public async Task RefusesAnUnknownVersion()
    {
        Assert.Equal(
            "This WIM file uses format version 0x10B00, which DDT does not support.",
            await RefusalAsync(new SyntheticWim { Version = 0x10B00 }));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public async Task RefusesASplitWim(int totalParts)
    {
        Assert.Equal(
            "Split WIM files (.swm) are not supported. Export the image into a single WIM first.",
            await RefusalAsync(new SyntheticWim { TotalParts = (ushort)totalParts }));
    }

    [Theory]
    [InlineData(0x04)]
    [InlineData(0x10)]
    [InlineData(0x06)]
    public async Task RefusesACompressedImageList(int flags)
    {
        Assert.Equal(
            "This WIM file stores its image list compressed, which DDT cannot read.",
            await RefusalAsync(new SyntheticWim { XmlFlags = (byte)flags }));
    }

    [Fact]
    public async Task RefusesAnImageListBeyondTheEndOfTheFile()
    {
        Assert.Equal("This WIM file is incomplete or damaged.", await RefusalAsync(new SyntheticWim { MissingXmlBytes = 2 }));
    }

    [Fact]
    public async Task RefusesAnEmptyImageList()
    {
        Assert.Equal("This WIM file is incomplete or damaged.", await RefusalAsync(new SyntheticWim { Xml = [] }));
    }

    [Theory]
    [InlineData("utf-16 without bom")]
    [InlineData("utf-16 big endian")]
    [InlineData("utf-8")]
    [InlineData("odd length")]
    public async Task RefusesAnImageListThatIsNotUtf16LittleEndianWithABom(string form)
    {
        string xml = "<WIM><IMAGE INDEX=\"1\"><NAME>Image</NAME></IMAGE></WIM>";
        byte[] bytes = form switch
        {
            "utf-16 without bom" => Encoding.Unicode.GetBytes(xml),
            "utf-16 big endian" => [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(xml)],
            "utf-8" => [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(xml)],
            _ => [.. SyntheticWim.Utf16(xml), 0x00],
        };

        Assert.Equal(
            "The image list in this WIM file is not UTF-16 text, which DDT cannot read.",
            await RefusalAsync(new SyntheticWim { Xml = bytes }));
    }

    [Fact]
    public async Task RefusesAnEncryptedEsd()
    {
        SyntheticWim wim = new()
        {
            Xml = SyntheticWim.Utf16("<WIM><ESD><ENCRYPTED>1</ENCRYPTED></ESD><IMAGE INDEX=\"1\"><NAME>Image</NAME></IMAGE></WIM>"),
        };

        Assert.Equal("This image is encrypted (an ESD from Windows Update) and cannot be applied.", await RefusalAsync(wim));
    }

    [Theory]
    [InlineData("<WIM><IMAGE INDEX=\"1\"><NAME>Image</IMAGE></WIM>")]
    [InlineData("<IMAGES><IMAGE INDEX=\"1\"/></IMAGES>")]
    [InlineData("<!DOCTYPE WIM [<!ENTITY name \"Image\">]><WIM><IMAGE INDEX=\"1\"><NAME>&name;</NAME></IMAGE></WIM>")]
    public async Task RefusesADamagedImageList(string xml)
    {
        Assert.Equal("The image list in this WIM file is damaged.", await RefusalAsync(new SyntheticWim { Xml = SyntheticWim.Utf16(xml) }));
    }

    [Theory]
    [InlineData(2, "<IMAGE INDEX=\"1\"/>")]
    [InlineData(1, "<IMAGE INDEX=\"1\"/><IMAGE INDEX=\"2\"/>")]
    [InlineData(2, "<IMAGE INDEX=\"1\"/><IMAGE INDEX=\"1\"/>")]
    [InlineData(2, "<IMAGE INDEX=\"1\"/><IMAGE INDEX=\"3\"/>")]
    [InlineData(1, "<IMAGE INDEX=\"0\"/>")]
    [InlineData(1, "<IMAGE/>")]
    [InlineData(1, "<IMAGE INDEX=\"one\"/>")]
    public async Task RefusesAnImageListThatDoesNotMatchTheHeader(int imageCount, string images)
    {
        SyntheticWim wim = new() { ImageCount = (uint)imageCount, Xml = SyntheticWim.Utf16($"<WIM>{images}</WIM>") };

        Assert.Equal("The image list in this WIM file does not match its header.", await RefusalAsync(wim));
    }
}
