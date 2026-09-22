// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.IO.Compression;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class PackageExtractorTests : IDisposable
{
    private const string Name = "package Dell drivers";

    // PK and the signatures of a local file header and a central directory entry.
    private static readonly byte[] s_localHeader = [0x50, 0x4B, 3, 4];
    private static readonly byte[] s_centralHeader = [0x50, 0x4B, 1, 2];

    private readonly string _directory = Directory.CreateTempSubdirectory("ddt-package-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Target => Path.Combine(_directory, "unpacked");

    [Fact]
    public async Task UnpacksFilesAndDirectories()
    {
        string zip = await WriteAsync(new TestZip(("setup.cmd", "echo hi"), ("drivers/", string.Empty), ("drivers/net.inf", "[Version]"), ("empty/", string.Empty)));

        await PackageExtractor.ExtractAsync(zip, Target, Name, long.MaxValue, TestContext.Current.CancellationToken);

        Assert.Equal("echo hi", await File.ReadAllTextAsync(Path.Combine(Target, "setup.cmd"), TestContext.Current.CancellationToken));
        Assert.Equal("[Version]", await File.ReadAllTextAsync(Path.Combine(Target, "drivers", "net.inf"), TestContext.Current.CancellationToken));
        Assert.True(Directory.Exists(Path.Combine(Target, "empty")));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("drivers/../../evil.txt")]
    [InlineData("/rooted.txt")]
    [InlineData("\\rooted.txt")]
    [InlineData("C:/Windows/evil.txt")]
    [InlineData("setup.cmd:hidden")]
    [InlineData("drivers//net.inf")]
    [InlineData("drivers/./net.inf")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("drivers/aux.inf")]
    [InlineData("LPT1.log")]
    [InlineData("COM\u00b9.txt")]
    [InlineData("nul .txt")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    public async Task RefusesANameWindowsWouldNotKeepInTheDirectory(string entry)
    {
        string zip = await WriteAsync(new TestZip(("setup.cmd", "echo hi"), (entry, "evil")));

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => PackageExtractor.ExtractAsync(zip, Target, Name, long.MaxValue, TestContext.Current.CancellationToken));

        Assert.Equal($"The {Name} holds an entry named {entry}, which the agent does not unpack.", exception.Message);
        Assert.False(Directory.Exists(Target));
    }

    [Fact]
    public async Task RefusesMoreThanTheDiskHasFree()
    {
        string zip = await WriteAsync(new TestZip(("a.txt", new string('a', 600)), ("b.txt", new string('b', 600))));

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => PackageExtractor.ExtractAsync(zip, Target, Name, 1000, TestContext.Current.CancellationToken));

        Assert.Equal($"The {Name} unpacks to 1.2 KB, but the disk has only 1000 bytes free.", exception.Message);
        Assert.False(Directory.Exists(Target));
    }

    [Fact]
    public async Task RefusesTheSameFileTwice()
    {
        string zip = await WriteAsync(new TestZip(("drivers/net.inf", "one"), ("DRIVERS/Net.inf", "two")));

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => PackageExtractor.ExtractAsync(zip, Target, Name, long.MaxValue, TestContext.Current.CancellationToken));

        Assert.Equal($"The {Name} holds DRIVERS/Net.inf twice, so it is not unpacked.", exception.Message);
    }

    // The zip says 10 bytes, in its local header and its central directory, for an entry that holds 1000. .NET cuts a
    // compressed entry off at its declared size itself; a stored one it reads to its stored length.
    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.Optimal)]
    public async Task NeverWritesMoreThanAnEntryDeclares(CompressionLevel compression)
    {
        using MemoryStream archive = new();

        using (ZipArchive creating = new(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream entry = creating.CreateEntry("big.txt", compression).Open();
            entry.Write(new byte[1000]);
        }

        byte[] content = archive.ToArray();
        int local = content.AsSpan().IndexOf(s_localHeader);
        int central = content.AsSpan().IndexOf(s_centralHeader);
        BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(local + 22), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(central + 24), 10);
        string zip = Path.Combine(_directory, "lying.zip");
        await File.WriteAllBytesAsync(zip, content, TestContext.Current.CancellationToken);

        Exception? exception = await Record.ExceptionAsync(
            () => PackageExtractor.ExtractAsync(zip, Target, Name, long.MaxValue, TestContext.Current.CancellationToken));

        FileInfo written = new(Path.Combine(Target, "big.txt"));
        Assert.True(!written.Exists || written.Length <= 10);

        if (compression == CompressionLevel.NoCompression)
        {
            Assert.Equal(
                $"The {Name} holds more at big.txt than the 10 bytes it declares, so it is not unpacked.",
                Assert.IsType<DeploymentStepException>(exception).Message);
        }
    }

    [Fact]
    public async Task RefusesAFileThatIsNoZip()
    {
        string zip = Path.Combine(_directory, "broken.zip");
        await File.WriteAllTextAsync(zip, "no zip", TestContext.Current.CancellationToken);

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => PackageExtractor.ExtractAsync(zip, Target, Name, long.MaxValue, TestContext.Current.CancellationToken));

        Assert.StartsWith($"The {Name} is not a zip file", exception.Message, StringComparison.Ordinal);
    }

    private async Task<string> WriteAsync(TestZip zip)
    {
        string path = Path.Combine(_directory, "package.zip");
        await File.WriteAllBytesAsync(path, zip.Content, TestContext.Current.CancellationToken);

        return path;
    }
}
