// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Security.Cryptography;
using DDT.Contracts.Packages;
using DDT.Server.Packages;
using Xunit;

namespace DDT.Server.Tests;

public sealed class PackageArchiveCheckTests
{
    private static PackageInspection Inspect(byte[] zip, PackageKind kind = PackageKind.Drivers)
    {
        using MemoryStream stream = new(zip);

        return PackageArchiveCheck.Inspect(stream, kind, TestContext.Current.CancellationToken);
    }

    private static void AssertRefused(PackageInspection inspection, string expectedStart)
    {
        Assert.NotNull(inspection.Refusal);
        Assert.StartsWith(expectedStart, inspection.Refusal, StringComparison.Ordinal);
        Assert.Equal(0, inspection.FileCount);
    }

    [Fact]
    public void CountsTheFilesAndTheBytesTheyUnpackTo()
    {
        byte[] zip = TestZip.Create(
            ("Audio/", null),
            ("Audio/Realtek/HDXRT.INF", new byte[1000]),
            ("Audio/Realtek/hdx.sys", RandomNumberGenerator.GetBytes(3000)),
            ("readme.txt", new byte[24]));

        Assert.Equal(new PackageInspection(3, 4024, null), Inspect(zip));
    }

    [Fact]
    public void AFilesPackageNeedsNoInfFile()
    {
        byte[] zip = TestZip.Create("setup.ps1", "tools/7z.exe");

        Assert.Null(Inspect(zip, PackageKind.Files).Refusal);
        AssertRefused(Inspect(zip, PackageKind.Drivers), "A driver package needs at least one .inf file.");
    }

    [Theory]
    [InlineData("../evil.inf", "has an empty name, or a folder name that points out of the package.")]
    [InlineData("drivers/../../evil.inf", "has an empty name, or a folder name that points out of the package.")]
    [InlineData("drivers/./evil.inf", "has an empty name, or a folder name that points out of the package.")]
    [InlineData("drivers//evil.inf", "has an empty name, or a folder name that points out of the package.")]
    [InlineData("/Windows/evil.inf", "starts at the root of a drive.")]
    [InlineData("\\Windows\\evil.inf", "starts at the root of a drive.")]
    [InlineData("C:/Windows/evil.inf", "has a character Windows does not allow in names")]
    [InlineData("drivers/evil.inf:hidden", "has a character Windows does not allow in names")]
    [InlineData("drivers/evil?.inf", "has a character Windows does not allow in names")]
    [InlineData("drivers/CON.inf", "has a name Windows keeps for a device")]
    [InlineData("nul", "has a name Windows keeps for a device")]
    [InlineData("com1/evil.inf", "has a name Windows keeps for a device")]
    [InlineData("drivers./evil.inf", "has a name that ends in a dot or a space")]
    [InlineData("drivers /evil.inf", "has a name that ends in a dot or a space")]
    [InlineData("drivers/evil\u0001.inf", "has a control character in its name.")]
    public void RefusesANameThatWouldLeaveThePackageOrThatWindowsCannotCreate(string name, string problem)
    {
        PackageInspection inspection = Inspect(TestZip.Create("good.inf", name));

        AssertRefused(inspection, "The entry ");
        Assert.Contains(problem, inspection.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesANameTooLongOrTooDeep()
    {
        AssertRefused(Inspect(TestZip.Create("good.inf", new string('a', 237) + ".inf")), "The entry ");
        Assert.Null(Inspect(TestZip.Create("good.inf", new string('a', 236) + ".inf")).Refusal);

        string deep = string.Concat(Enumerable.Repeat("d/", 32)) + "x.inf";
        Assert.EndsWith("is more than 32 folders deep.", Inspect(TestZip.Create("good.inf", deep)).Refusal, StringComparison.Ordinal);
        Assert.Null(Inspect(TestZip.Create("good.inf", string.Concat(Enumerable.Repeat("d/", 31)) + "x.inf")).Refusal);
    }

    [Fact]
    public void RefusesTwoEntriesThatWindowsTakesForOne()
    {
        AssertRefused(Inspect(TestZip.Create("Drivers/A.inf", "drivers/a.INF")), "The entry drivers/a.INF is in the zip twice");
        AssertRefused(Inspect(TestZip.Create("good.inf", "x", "x/y.inf")), "The zip has a file x and a folder of the same name.");
        AssertRefused(Inspect(TestZip.Create("good.inf", "x/", "x")), "The entry x is in the zip twice");
    }

    [Fact]
    public void RefusesAnEncryptedEntry()
    {
        byte[] zip = TestZip.Create("good.inf", "secret.sys");
        TestZip.PatchCentral(zip, 1, TestZip.Flags, 1, bytes: 2);

        AssertRefused(Inspect(zip), "The entry secret.sys is encrypted.");
    }

    [Fact]
    public void RefusesASymbolicLink()
    {
        byte[] zip = TestZip.Create(archive =>
        {
            archive.CreateEntry("good.inf");
            archive.CreateEntry("link.sys").ExternalAttributes = unchecked((int)0xA1FF0000);
        });

        AssertRefused(Inspect(zip), "The entry link.sys is a symbolic link.");
    }

    [Fact]
    public void RefusesAnEntryThatUnpacksToOtherBytesThanItSays()
    {
        byte[] zip = TestZip.Create(("good.inf", new byte[5000]));
        TestZip.PatchCentral(zip, 0, TestZip.UncompressedSize, 6000);

        PackageInspection inspection = Inspect(zip);

        Assert.NotNull(inspection.Refusal);
        Assert.StartsWith("The entry good.inf ", inspection.Refusal, StringComparison.Ordinal);
    }

    // A reader stops at the size the zip states.
    // A smaller size reads as that many bytes without any error, so an agent would unpack a truncated driver.
    [Fact]
    public void RefusesAnEntryThatSaysItIsSmallerThanItIs()
    {
        byte[] zip = TestZip.Create(("good.inf", new byte[50_000]));
        TestZip.PatchCentral(zip, 0, TestZip.UncompressedSize, 10);

        AssertRefused(Inspect(zip), "The entry good.inf does not unpack to the bytes the zip says it holds.");
    }

    // Each entry says it unpacks to just under 4 GB, together more than the limit, so nothing is inflated at all.
    [Fact]
    public void RefusesAZipThatSaysItUnpacksToMoreThanTheLimit()
    {
        byte[] zip = TestZip.Create(Enumerable.Range(0, 17).Select(i => $"part{i}.inf").ToArray());

        for (int entry = 0; entry < 17; entry++)
        {
            TestZip.PatchCentral(zip, entry, TestZip.UncompressedSize, 0xFFFFFFFE);
        }

        AssertRefused(Inspect(zip), "Unpacked, the zip would take more than 64 GB.");
    }

    [Fact]
    public void RefusesMoreEntriesThanAPackageHolds()
    {
        byte[] zip = TestZip.Create(archive =>
        {
            archive.CreateEntry("good.inf");

            for (int index = 0; index < PackageLimits.MaxEntries; index++)
            {
                archive.CreateEntry($"f/{index}", CompressionLevel.NoCompression);
            }
        });

        AssertRefused(Inspect(zip), $"The zip holds {PackageLimits.MaxEntries + 1} entries.");
    }

    [Fact]
    public void RefusesWhatIsNotAZipOrCannotBeUnpacked()
    {
        AssertRefused(Inspect(RandomNumberGenerator.GetBytes(4096)), "The file is not a zip archive, or it is damaged.");

        byte[] unknownMethod = TestZip.Create("good.inf");
        TestZip.PatchCentral(unknownMethod, 0, TestZip.Method, 99, bytes: 2);
        Assert.NotNull(Inspect(unknownMethod).Refusal);
    }
}
