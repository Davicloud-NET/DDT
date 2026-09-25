// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using DDT.Contracts.Images;
using DDT.Core.Disks;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class RawImageUploadTests(ConversionToolsApplication application) : IClassFixture<ConversionToolsApplication>
{
    private const int ChunkBytes = 1024 * 1024;

    private static readonly byte[] s_microsoftShim = TestPe.Fixture("shimx64.efi.dualsigned");
    private static readonly byte[] s_canonicalFallback = TestPe.Fixture("fbx64.efi");

    private ImageStore Store => application.Services.GetRequiredService<ImageStore>();

    private async Task<HttpResponseMessage> UploadAsync(byte[] file, string fileName)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ImageUploadSession session = await administrator.UploadAsync(file, ChunkBytes, fileName);

        return await administrator.CompleteUploadAsync(session.Id);
    }

    private async Task<ImageSummary> AddedAsync(byte[] file, string fileName)
    {
        HttpResponseMessage completed = await UploadAsync(file, fileName);
        Assert.Equal(HttpStatusCode.Created, completed.StatusCode);

        return Assert.Single(await RegisteredMachine.ReadAsync<IReadOnlyList<ImageSummary>>(completed));
    }

    // With startOnly, the reason is the start of what the server says, whose end comes from the framework.
    private async Task AssertRefusedAsync(byte[] file, string fileName, string reason, bool startOnly = false)
    {
        HttpResponseMessage refused = await UploadAsync(file, fileName);
        string? title = (await refused.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options))?.Title;

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);

        if (startOnly)
        {
            Assert.StartsWith(reason, title, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(reason, title);
        }
    }

    // A cause on the server keeps the upload: completed again, it answers the same until the cause is gone, and it can
    // be discarded.
    private async Task AssertKeptAsync(byte[] file, string fileName, string reason)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ImageUploadSession session = await administrator.UploadAsync(file, ChunkBytes, fileName);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            HttpResponseMessage kept = await administrator.CompleteUploadAsync(session.Id);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, kept.StatusCode);
            Assert.Equal(reason, (await kept.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options))?.Title);
            Assert.True(File.Exists(Store.PartPath(session.Id)), $"The upload of {fileName} is gone.");
        }

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"/api/images/uploads/{session.Id:D}")).StatusCode);
    }

    [Fact]
    public async Task AddsACloudImageSignedForSecureBootCompressedWithZstd()
    {
        byte[] disk = TestDisk.Create(new Dictionary<string, byte[]> { [@"EFI\BOOT\BOOTX64.EFI"] = s_microsoftShim }, seed: 11);
        string name = $"noble-{Guid.NewGuid():N}";

        ImageSummary image = await AddedAsync(disk, $"{name}.img");

        Assert.Equal(name, image.Name);
        Assert.Equal(ImageKind.RawDisk, image.Kind);
        Assert.Equal(0, image.WimIndex);
        Assert.Equal("x64", image.Architecture);
        Assert.Equal(disk.Length, image.InstalledBytes);
        Assert.Equal(ImageBootCapability.SecureBootOk, image.BootCapability);
        Assert.Equal(
            @"\EFI\BOOT\BOOTX64.EFI is signed by Microsoft Windows UEFI Driver Publisher under Microsoft's UEFI CA, which PCs trust unless their firmware turns it off, as Secured-core PCs do.",
            image.BootDetail);
        Assert.Equal(TestDisk.Sha256(disk), image.SourceSha256);

        byte[] stored = await File.ReadAllBytesAsync(Store.ObjectPath(image.Sha256), TestContext.Current.CancellationToken);
        Assert.Equal(image.Sha256, TestDisk.Sha256(stored));
        Assert.Equal(stored.Length, image.SizeBytes);
        Assert.True(stored.Length < disk.Length / 4);
        Assert.Equal(disk, TestDisk.Unzstd(stored));
        Assert.Empty(Directory.EnumerateFiles(Store.UploadsDirectory, "*.raw").Concat(Directory.EnumerateFiles(Store.UploadsDirectory, "*.zst")));

        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        AuditEvent audit = await database.AuditEvents.SingleAsync(
            e => e.Action == AuditActions.ImageUploaded && e.SubjectId == image.Id.ToString("D"),
            TestContext.Current.CancellationToken);
        Assert.StartsWith($"{name}, a raw disk image of {disk.Length} bytes from {name}.img, SecureBootOk,", audit.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhyAnImageDoesNotStartWithSecureBootOn()
    {
        byte[] canonical = TestDisk.Create(
            new Dictionary<string, byte[]>
            {
                [@"EFI\BOOT\BOOTX64.EFI"] = s_canonicalFallback,
                [@"EFI\ubuntu\shimx64.efi"] = s_microsoftShim,
            },
            seed: 12);
        byte[] unsigned = TestDisk.Create(new Dictionary<string, byte[]> { [@"EFI\BOOT\BOOTX64.EFI"] = TestPe.Create() }, seed: 13);

        ImageSummary byCanonical = await AddedAsync(canonical, $"canonical-{Guid.NewGuid():N}.raw");
        ImageSummary notSigned = await AddedAsync(unsigned, $"unsigned-{Guid.NewGuid():N}.raw");

        Assert.Equal(ImageBootCapability.NotSigned, byCanonical.BootCapability);
        Assert.Equal(
            @"\EFI\BOOT\BOOTX64.EFI is signed by Canonical Ltd. Secure Boot Signing (2022 v1), which Microsoft's UEFI CA did not certify. " +
            @"\EFI\ubuntu\shimx64.efi is signed under Microsoft's UEFI CA, but DDT's boot entry starts \EFI\BOOT\BOOTX64.EFI.",
            byCanonical.BootDetail);
        Assert.Equal(ImageBootCapability.NotSigned, notSigned.BootCapability);
        Assert.Equal(@"\EFI\BOOT\BOOTX64.EFI carries no signature.", notSigned.BootDetail);
        Assert.Equal("x64", notSigned.Architecture);
    }

    [Fact]
    public async Task SaysWhenItCannotTellWhetherAnImageStarts()
    {
        byte[] noEsp = TestDisk.Create(new Dictionary<string, byte[]>(), seed: 14, withEsp: false);
        byte[] arm = TestDisk.Create(new Dictionary<string, byte[]> { [@"EFI\BOOT\BOOTAA64.EFI"] = TestPe.Create(PeImage.MachineArm64) }, seed: 15);
        byte[] empty = TestDisk.Create(new Dictionary<string, byte[]> { [@"EFI\ubuntu\grub.cfg"] = [1] }, seed: 16);

        ImageSummary withoutEsp = await AddedAsync(noEsp, $"no-esp-{Guid.NewGuid():N}.img");
        ImageSummary forArm = await AddedAsync(arm, $"arm-{Guid.NewGuid():N}.img");
        ImageSummary withoutLoader = await AddedAsync(empty, $"empty-{Guid.NewGuid():N}.img");

        Assert.Equal(ImageBootCapability.Unknown, withoutEsp.BootCapability);
        Assert.Equal("The image has no EFI system partition. DDT cannot tell whether it starts with Secure Boot on.", withoutEsp.BootDetail);
        Assert.Null(withoutEsp.Architecture);
        Assert.Equal(ImageBootCapability.Unknown, forArm.BootCapability);
        Assert.Equal("arm64", forArm.Architecture);
        Assert.Equal(@"The image is for arm64 machines: it has \EFI\BOOT\BOOTAA64.EFI and no \EFI\BOOT\BOOTX64.EFI.", forArm.BootDetail);
        Assert.Equal(@"The image has no \EFI\BOOT\BOOTX64.EFI, the file DDT's boot entry starts.", withoutLoader.BootDetail);
    }

    [Fact]
    public async Task ReadsGzipAndZstdAndAddsTheSameDiskOnce()
    {
        byte[] disk = TestDisk.Create(new Dictionary<string, byte[]> { [@"EFI\BOOT\BOOTX64.EFI"] = s_microsoftShim }, seed: 17);
        string name = $"debian-{Guid.NewGuid():N}";

        ImageSummary added = await AddedAsync(TestDisk.Gzip(disk), $"{name}.raw.gz");
        HttpResponseMessage again = await UploadAsync(TestDisk.Zstd(disk), $"{name}.img.zst");

        Assert.Equal(name, added.Name);
        Assert.Equal(TestDisk.Sha256(disk), added.SourceSha256);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal([added], await RegisteredMachine.ReadAsync<IReadOnlyList<ImageSummary>>(again));
        Assert.Equal(disk, TestDisk.Unzstd(await File.ReadAllBytesAsync(Store.ObjectPath(added.Sha256), TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task PutsBackAStoredDiskThatWentMissingWhenTheSameDiskIsUploadedAgain()
    {
        byte[] disk = TestDisk.Create(new Dictionary<string, byte[]>(), seed: 19);
        ImageSummary added = await AddedAsync(disk, $"lost-{Guid.NewGuid():N}.img");
        File.Delete(Store.ObjectPath(added.Sha256));

        HttpResponseMessage again = await UploadAsync(TestDisk.Gzip(disk), "lost-again.img.gz");

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        ImageSummary existing = Assert.Single(await RegisteredMachine.ReadAsync<IReadOnlyList<ImageSummary>>(again));
        Assert.Equal(added.Id, existing.Id);
        Assert.True(File.Exists(Store.ObjectPath(existing.Sha256)), "The stored disk was not put back.");
    }

    [Fact]
    public async Task RefusesFilesThatAreNoDiskImageDdtCanUse()
    {
        byte[] disk = TestDisk.Create(new Dictionary<string, byte[]>(), seed: 18);
        byte[] noise = new byte[64 * 1024];
        new Random(18).NextBytes(noise);
        byte[] vhdx = [.. "vhdxfile"u8, .. new byte[4096]];

        await AssertRefusedAsync(noise, "noise.img", RawImageImporter.NotAnImageMessage);
        await AssertRefusedAsync(
            [.. "WLPWM\0\0\0"u8, .. new byte[4096]],
            "pipable.wim",
            "Pipable WIM files are not supported. Export the image into a regular WIM first.");
        await AssertRefusedAsync(TestDisk.ForFourKilobyteSectors(disk), "4k.img", GptLayout.FourKilobyteSectorsMessage);
        await AssertRefusedAsync(
            vhdx,
            "disk.vhdx",
            "This is a VHDX disk image, which DDT does not read. Convert it with qemu-img convert -O raw <file> disk.raw and upload " +
            "disk.raw, or upload the distribution's raw or qcow2 image.");
        byte[] damaged = TestDisk.Gzip(disk);
        damaged.AsSpan(damaged.Length / 2, 64).Fill(0xFF);
        await AssertRefusedAsync(damaged, "damaged.img.gz", "The compressed file is damaged: ", startOnly: true);
        await AssertRefusedAsync(
            TestDisk.Gzip(disk)[..30_000],
            "cut.img.gz",
            "The disk image holds ",
            startOnly: true);
        Assert.Empty(Directory.EnumerateFiles(Store.UploadsDirectory, "*.raw").Concat(Directory.EnumerateFiles(Store.UploadsDirectory, "*.zst")));
    }

    [Fact]
    public async Task RefusesQcow2AndXzWithoutTheirToolsAndQcow2ThatReadsOtherFiles()
    {
        application.Tools.Clear();

        await AssertKeptAsync(
            Qcow2(backingFile: 0),
            "noble.qcow2",
            "This is a qcow2 image, and qemu-img is not installed on the server. Install qemu-img there and complete the upload " +
            "again, or convert the file with qemu-img convert -O raw <file> disk.raw and upload disk.raw.");
        await AssertRefusedAsync(
            Qcow2(backingFile: 512),
            "layer.qcow2",
            "The qcow2 image depends on a backing file. Make a standalone image with qemu-img convert -O qcow2 <file> standalone.qcow2 " +
            "and upload that.");
        await AssertKeptAsync(
            [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00, .. new byte[1000]],
            "disk.raw.xz",
            "This file is compressed with xz, which is not installed on the server. Install xz there and complete the upload again, " +
            "or unpack the file with xz -d and upload the disk image it holds.");
    }

    [Fact]
    public async Task UnpacksXzWithTheInstalledTool()
    {
        string? xz = new ConversionTools().Find(ConversionTools.Xz);
        Assert.SkipWhen(xz is null, "xz is not installed.");

        byte[] disk = TestDisk.Create(new Dictionary<string, byte[]> { [@"EFI\BOOT\BOOTX64.EFI"] = s_microsoftShim }, seed: 19);
        application.Tools[ConversionTools.Xz] = xz!;

        ImageSummary added = await AddedAsync(Xz(xz!, disk), $"fedora-{Guid.NewGuid():N}.raw.xz");

        Assert.Equal(TestDisk.Sha256(disk), added.SourceSha256);
        Assert.Equal(ImageBootCapability.SecureBootOk, added.BootCapability);
    }

    // A qcow2 header, version 3, of a 16 MiB disk, backed by another file when backingFile is not 0.
    private static byte[] Qcow2(ulong backingFile)
    {
        byte[] header = new byte[4096];
        header[0] = 0x51;
        header[1] = 0x46;
        header[2] = 0x49;
        header[3] = 0xFB;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(8), backingFile);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), 16);
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(24), 16 * 1024 * 1024);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(100), 104);

        return header;
    }

    private static byte[] Xz(string xz, byte[] disk)
    {
        using Process process = Process.Start(new ProcessStartInfo(xz, "--compress --stdout -0")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;

        Task<byte[]> reading = Task.Run(async () =>
        {
            using MemoryStream output = new();
            await process.StandardOutput.BaseStream.CopyToAsync(output);

            return output.ToArray();
        });

        process.StandardInput.BaseStream.Write(disk);
        process.StandardInput.Close();
        byte[] compressed = reading.GetAwaiter().GetResult();
        process.WaitForExit();

        return compressed;
    }
}
