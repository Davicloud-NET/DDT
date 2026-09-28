// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Runtime.Versioning;
using DDT.Core.Wim;
using Xunit;

namespace DDT.Core.Tests.Wim;

// Runs the real libwim-15.dll from the ManagedWimLib package. On Windows these tests fail, never skip, when it
// cannot be loaded.
[SupportedOSPlatform("windows")]
public sealed class WimLibraryTests : IDisposable
{
    private const string ImageName = "DDT test image";
    private const byte CompressedResource = 0x04;
    private const byte SolidResource = 0x10;

    // A resource header of 24 bytes, whose flags are its byte 7, then part number, reference count and SHA-1.
    private const int BlobTableEntryBytes = 50;

    private readonly string _root = Directory.CreateTempSubdirectory("ddt-wim-").FullName;

    private string Source => Path.Combine(_root, "source");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // Test runs are not elevated, and strict needs the backup and restore privileges.
    private static WimLibrary CreateLibrary()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "wimlib is only tested on Windows.");

        return new WimLibrary(strict: false, errorLogPath: null);
    }

    private async Task<string> CaptureAsync(WimLibrary library, IProgress<WimProgress>? progress = null)
    {
        string wim = Path.Combine(_root, "captured.wim");

        TestTree.Create(Source);
        await library.CaptureAsync(new WimCapture(Source, wim, ImageName, WimCompression.Lzx), progress, Token);

        return wim;
    }

    [Fact]
    public async Task RoundTripsATreeThroughCaptureExportAndApply()
    {
        WimLibrary library = CreateLibrary();
        string captured = await CaptureAsync(library);
        string exported = Path.Combine(_root, "exported.esd");
        string appliedFromCaptured = Path.Combine(_root, "applied-lzx");
        string appliedFromExported = Path.Combine(_root, "applied-lzms");

        await library.ExportAsync(new WimExport(captured, 1, exported, WimCompression.Lzms), null, Token);
        await library.ApplyAsync(captured, 1, appliedFromCaptured, null, Token);
        await library.ApplyAsync(exported, 1, appliedFromExported, null, Token);

        IReadOnlyList<string> source = TestTree.Describe(Source);

        Assert.Equal(source, TestTree.Describe(appliedFromCaptured));
        Assert.Equal(source, TestTree.Describe(appliedFromExported));
    }

    [Fact]
    public async Task WimMetadataReadsWhatWimlibWrote()
    {
        WimLibrary library = CreateLibrary();
        string captured = await CaptureAsync(library);
        string exported = Path.Combine(_root, "exported.esd");

        await library.ExportAsync(new WimExport(captured, 1, exported, WimCompression.Lzms), null, Token);

        foreach (string wim in (string[])[captured, exported])
        {
            await using FileStream stream = File.OpenRead(wim);
            WimImageInfo image = Assert.Single(await WimMetadata.ReadAsync(stream, Token));

            Assert.Equal(1, image.Index);
            Assert.Equal(ImageName, image.Name);
            Assert.Null(image.Architecture);
            Assert.Equal(TestTree.TotalFileBytes(Source), image.TotalBytes);
            Assert.Equal(TestTree.RandomBytes, image.HardLinkBytes);
        }
    }

    [Fact]
    public async Task ExportsLzmsAsASolidWim()
    {
        WimLibrary library = CreateLibrary();
        string captured = await CaptureAsync(library);
        string exported = Path.Combine(_root, "exported.esd");

        await library.ExportAsync(new WimExport(captured, 1, exported, WimCompression.Lzms), null, Token);

        // wimlib writes version 0xE00 for any LZMS output, so only a solid entry in the blob table shows solid mode.
        // The table's resource header is at 48: size in the low 56 bits, flags in the top byte, offset at 56.
        byte[] wim = await File.ReadAllBytesAsync(exported, Token);
        ulong blobTable = BinaryPrimitives.ReadUInt64LittleEndian(wim.AsSpan(48));
        int blobTableSize = checked((int)(blobTable & 0x00FF_FFFF_FFFF_FFFF));
        int blobTableOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(wim.AsSpan(56)));

        Assert.Equal(0xE00u, BinaryPrimitives.ReadUInt32LittleEndian(wim.AsSpan(12)));
        Assert.Equal(0, (byte)(blobTable >> 56) & CompressedResource);
        Assert.Equal(0, blobTableSize % BlobTableEntryBytes);
        Assert.Contains(
            new ArraySegment<byte>(wim, blobTableOffset, blobTableSize).Chunk(BlobTableEntryBytes),
            entry => (entry[7] & SolidResource) != 0);
    }

    [Fact]
    public async Task ReportsProgressUpToTheTotal()
    {
        WimLibrary library = CreateLibrary();
        RecordingProgress capture = new();
        RecordingProgress export = new();
        RecordingProgress apply = new();
        string captured = await CaptureAsync(library, capture);

        await library.ExportAsync(new WimExport(captured, 1, Path.Combine(_root, "exported.esd"), WimCompression.Lzms), export, Token);
        await library.ApplyAsync(captured, 1, Path.Combine(_root, "applied"), apply, Token);

        // Every operation counts the hard-linked file once.
        long uniqueBytes = TestTree.TotalFileBytes(Source) - TestTree.RandomBytes;

        foreach (RecordingProgress progress in (RecordingProgress[])[capture, export, apply])
        {
            Assert.NotEmpty(progress.Reports);
            Assert.True(progress.Reports[0].CompletedBytes < progress.Reports[0].TotalBytes);
            Assert.All(progress.Reports, report => Assert.Equal(uniqueBytes, report.TotalBytes));
            Assert.All(progress.Reports, report => Assert.True(report.CompletedBytes <= report.TotalBytes));
            Assert.Equal(progress.Reports[^1].TotalBytes, progress.Reports[^1].CompletedBytes);
            Assert.All(progress.Reports.Zip(progress.Reports.Skip(1)), pair => Assert.True(pair.First.CompletedBytes <= pair.Second.CompletedBytes));
        }
    }

    [Fact]
    public async Task CancellingDuringAnApplyAbortsIt()
    {
        WimLibrary library = CreateLibrary();
        string captured = await CaptureAsync(library);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        RecordingProgress progress = new(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => library.ApplyAsync(captured, 1, Path.Combine(_root, "applied"), progress, cancellation.Token));

        Assert.Single(progress.Reports);
    }

    [Fact]
    public async Task AProgressHandlerThatThrowsAbortsTheApplyWithItsException()
    {
        WimLibrary library = CreateLibrary();
        string captured = await CaptureAsync(library);
        RecordingProgress progress = new(_ => throw new InvalidOperationException("The handler failed."));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => library.ApplyAsync(captured, 1, Path.Combine(_root, "applied"), progress, Token));

        Assert.Equal("The handler failed.", exception.Message);
    }

    [Fact]
    public async Task RefusesAFileThatIsNotAWim()
    {
        WimLibrary library = CreateLibrary();
        string notAWim = Path.Combine(_root, "not.wim");
        string target = Path.Combine(_root, "applied");

        await File.WriteAllTextAsync(notAWim, "definitely not a wim", Token);

        WimLibraryException exception = await Assert.ThrowsAsync<WimLibraryException>(
            () => library.ApplyAsync(notAWim, 1, target, null, Token));

        Assert.Equal(65, exception.ErrorCode);
        Assert.Equal(
            $"Image 1 of {notAWim} could not be applied to {target}: Unexpectedly reached the end of the file (wimlib error 65).",
            exception.Message);
    }

    [Fact]
    public async Task RefusesAnImageTheWimDoesNotHold()
    {
        WimLibrary library = CreateLibrary();
        string captured = await CaptureAsync(library);

        WimLibraryException exception = await Assert.ThrowsAsync<WimLibraryException>(
            () => library.ApplyAsync(captured, 2, Path.Combine(_root, "applied"), null, Token));

        Assert.Equal(18, exception.ErrorCode);
    }

    [Fact]
    public async Task NamesTheFileAnApplyFailedOn()
    {
        WimLibrary library = CreateLibrary();
        string captured = await CaptureAsync(library);
        string target = Path.Combine(_root, "applied");
        string blocked = Path.Combine(target, "hello.txt");

        Directory.CreateDirectory(target);

        WimLibraryException exception;
        await using (new FileStream(blocked, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            exception = await Assert.ThrowsAsync<WimLibraryException>(() => library.ApplyAsync(captured, 1, target, null, Token));
        }

        Assert.NotNull(exception.FailingPath);
        Assert.EndsWith("hello.txt", exception.FailingPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith($" The failing path is {exception.FailingPath}.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnotherStrictnessInTheSameProcess()
    {
        CreateLibrary();

        WimLibraryException exception = Assert.Throws<WimLibraryException>(() => new WimLibrary(strict: true, errorLogPath: null));

        Assert.Equal(
            "wimlib is set up for this process without strict privileges and without an error log, " +
            "so it cannot also run with strict privileges and without an error log.",
            exception.Message);
    }

    [Fact]
    public void RefusesAnotherErrorLogInTheSameProcess()
    {
        CreateLibrary();
        string log = Path.Combine(_root, "wimlib.log");

        WimLibraryException exception = Assert.Throws<WimLibraryException>(() => new WimLibrary(strict: false, errorLogPath: log));

        Assert.Equal(
            "wimlib is set up for this process without strict privileges and without an error log, " +
            $"so it cannot also run without strict privileges and with the error log {log}.",
            exception.Message);
    }
}
