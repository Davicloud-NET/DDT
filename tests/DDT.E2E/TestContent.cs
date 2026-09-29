// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DDT.Core.Disks;

namespace DDT.E2E;

// What the tests upload. Random bytes make every file new to the library. Their size decides how long an agent takes
// to download a file, which is how long a test has to act while a step runs.
internal static class TestContent
{
    private const int WimHeaderLength = 208;
    private const int Megabyte = 1024 * 1024;

    // Only as much of a WIM as the server and a dry run read. That's the header, filler data, and an image list naming
    // one x64 image, in UTF-16 LE with a byte order mark. A dry run applies nothing, so no real image is needed.
    public static void WriteWim(string path, int dataMegabytes, string imageName = "DDT E2E Windows")
    {
        byte[] list =
        [
            0xFF, 0xFE,
            .. Encoding.Unicode.GetBytes(
                "<WIM><TOTALBYTES>1</TOTALBYTES><IMAGE INDEX=\"1\"><TOTALBYTES>10000000</TOTALBYTES><WINDOWS><ARCH>9</ARCH>" +
                "<EDITIONID>Professional</EDITIONID><LANGUAGES><LANGUAGE>en-US</LANGUAGE><DEFAULT>en-US</DEFAULT></LANGUAGES>" +
                "<VERSION><MAJOR>10</MAJOR><MINOR>0</MINOR><BUILD>26100</BUILD><SPBUILD>1</SPBUILD></VERSION></WINDOWS>" +
                $"<NAME>{imageName}</NAME></IMAGE></WIM>"),
        ];
        long dataLength = (long)dataMegabytes * Megabyte;
        byte[] header = new byte[WimHeaderLength];

        "MSWIM\0\0\0"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), WimHeaderLength);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 0x10D00);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 0x2 | 0x40000);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), 32768);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(42), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), 1);

        // The image list's resource: its size in the file without flags, its offset and its size.
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(72), (ulong)list.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(80), (ulong)(WimHeaderLength + dataLength));
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(88), (ulong)list.Length);

        using FileStream file = File.Create(path);
        file.Write(header);
        WriteRandom(file, dataLength);
        file.Write(list);
    }

    // Laid out and gzipped the way distributions publish cloud images. It has an EFI system partition at 1 MiB with an
    // unsigned x64 \EFI\BOOT\BOOTX64.EFI, a root partition of rootMegabytes of random bytes, and the backup table at
    // the end.
    public static void WriteRawImage(string path, int rootMegabytes)
    {
        const long espFirst = 2048;
        const long espSectors = 8192;
        long rootFirst = espFirst + espSectors;
        long rootSectors = rootMegabytes * 2048L;
        long sectors = rootFirst + rootSectors + 2048;

        GptLayout layout = GptLayout.Create(sectors, Guid.NewGuid())
            .WithPartition(GptPartitionTypes.EfiSystem, Guid.NewGuid(), "EFI", espFirst, espFirst + espSectors - 1)
            .WithPartition(GptPartitionTypes.LinuxFileSystem, Guid.NewGuid(), "root", rootFirst, rootFirst + rootSectors - 1);
        byte[] disk = new byte[sectors * GptLayout.SectorSize];

        void Place(long lba, byte[] content) => content.CopyTo(disk, lba * GptLayout.SectorSize);

        Place(0, layout.ProtectiveMbr(default));
        Place(1, layout.PrimaryHeader());
        Place(layout.EntriesLba, layout.EntryArray());
        Place(layout.BackupEntriesLba, layout.EntryArray());
        Place(layout.BackupLba, layout.BackupHeader());

        FatVolumeBuilder esp = new(espSectors * GptLayout.SectorSize, "UEFI", 0x0DD7E2E0, DateTime.UtcNow) { HiddenSectors = espFirst };
        esp.AddFile(@"EFI\BOOT\BOOTX64.EFI", UnsignedEfiProgram());
        Place(espFirst, esp.Build());
        RandomNumberGenerator.Fill(disk.AsSpan((int)(rootFirst * GptLayout.SectorSize), (int)(rootSectors * GptLayout.SectorSize)));

        using FileStream file = File.Create(path);
        using GZipStream gzip = new(file, CompressionLevel.Fastest);
        gzip.Write(disk);
    }

    // A driver package needs an .inf, which the server checks for.
    public static void WriteDriverPackage(string path)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(zip, "e2e/e2e.inf", Encoding.ASCII.GetBytes("[Version]\r\nSignature=\"$WINDOWS NT$\"\r\nClass=System\r\n"));
        WriteEntry(zip, "e2e/e2e.sys", RandomNumberGenerator.GetBytes(4096));
    }

    // A files package with a readme, and dataMegabytes of random data stored uncompressed.
    public static void WriteFilesPackage(string path, int dataMegabytes)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(zip, "readme.txt", Encoding.ASCII.GetBytes($"DDT E2E files {Guid.NewGuid():N}\r\n"));

        if (dataMegabytes > 0)
        {
            using Stream data = zip.CreateEntry("data.bin", CompressionLevel.NoCompression).Open();
            WriteRandom(data, (long)dataMegabytes * Megabyte);
        }
    }

    // The smallest x64 PE32+ file the server reads. It has headers in the first 512 bytes, then one 512-byte section,
    // and no certificate table.
    private static byte[] UnsignedEfiProgram()
    {
        const int peOffset = 0x40;
        const int optionalHeader = peOffset + 24;
        const int section = optionalHeader + 240;
        byte[] file = new byte[1024];
        Span<byte> span = file;

        span[0] = (byte)'M';
        span[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(span[0x3C..], peOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(span[peOffset..], 0x00004550);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(peOffset + 4)..], 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(peOffset + 6)..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(peOffset + 20)..], 240);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(peOffset + 22)..], 0x22);
        BinaryPrimitives.WriteUInt16LittleEndian(span[optionalHeader..], 0x20B);
        BinaryPrimitives.WriteInt32LittleEndian(span[(optionalHeader + 60)..], 0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(optionalHeader + 68)..], 10);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(optionalHeader + 108)..], 16);
        ".text"u8.CopyTo(span[section..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[(section + 8)..], 0x200);
        BinaryPrimitives.WriteInt32LittleEndian(span[(section + 12)..], 0x1000);
        BinaryPrimitives.WriteInt32LittleEndian(span[(section + 16)..], 0x200);
        BinaryPrimitives.WriteInt32LittleEndian(span[(section + 20)..], 0x200);
        RandomNumberGenerator.Fill(span[0x200..]);

        return file;
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] content)
    {
        using Stream entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        entry.Write(content);
    }

    private static void WriteRandom(Stream stream, long length)
    {
        byte[] buffer = new byte[Megabyte];

        for (long written = 0; written < length; written += buffer.Length)
        {
            RandomNumberGenerator.Fill(buffer);
            stream.Write(buffer, 0, (int)Math.Min(buffer.Length, length - written));
        }
    }
}
