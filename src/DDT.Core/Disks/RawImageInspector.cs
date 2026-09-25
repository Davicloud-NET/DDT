// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Core.Disks;

// Reads what DDT needs from a raw disk image before storing it. An image without a readable GUID partition table is
// refused; one whose EFI system partition cannot be read is not, as it may still start, but says why in BootProblem.
// The boot files are the fallback files in \EFI\BOOT, which firmware starts from a disk without a boot entry, and the
// shims beside a distribution's own boot loader, such as \EFI\ubuntu\shimx64.efi.
public static class RawImageInspector
{
    public const string BootDirectory = @"\EFI\BOOT";
    public const string FallbackFile = "BOOTX64.EFI";

    public const int MaxBootFileBytes = 32 * 1024 * 1024;

    private const int MaxBootFiles = 16;

    // Throws InvalidGptException, whose message says why the image is refused.
    public static RawImageInfo Inspect(Stream image)
    {
        ArgumentNullException.ThrowIfNull(image);

        long length = image.Length;

        // Enough to see a table made for 4 KiB sectors, which starts at 4096.
        byte[] first = ReadHead(image, Math.Clamp(length, 2 * GptLayout.SectorSize, 8192));
        byte[] head = ReadHead(image, GptLayout.HeadBytesFor(first));
        GptLayout table = GptLayout.Read(head);
        long end = (table.LastUsedLba + 1) * GptLayout.SectorSize;

        if (end > length)
        {
            throw new InvalidGptException(string.Create(
                CultureInfo.InvariantCulture,
                $"The disk image holds {length} bytes, but its partitions reach to byte {end}. The file is incomplete."));
        }

        GptPartition? system = table.Partitions.FirstOrDefault(partition => partition.Type == GptPartitionTypes.EfiSystem);

        if (system is null)
        {
            return new RawImageInfo(length, table, null, [], "The image has no EFI system partition.");
        }

        FatVolume volume;

        try
        {
            volume = FatVolume.Open(image, system.FirstLba * GptLayout.SectorSize, system.Sectors * GptLayout.SectorSize);
        }
        catch (InvalidDataException exception)
        {
            return new RawImageInfo(length, table, system, [], $"The image's EFI system partition cannot be read: {exception.Message}");
        }

        List<RawImageBootFile> files = [];
        List<string> unreadable = [];
        string? problem = null;

        try
        {
            foreach ((string path, FatEntry entry) in BootFileEntries(volume).Take(MaxBootFiles))
            {
                try
                {
                    files.Add(new RawImageBootFile(path, volume.ReadFile(entry, MaxBootFileBytes)));
                }
                catch (InvalidDataException exception)
                {
                    unreadable.Add(path);
                    problem ??= $"{path} cannot be read: {exception.Message}";
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or DirectoryNotFoundException)
        {
            problem ??= $"The image's EFI system partition cannot be read: {exception.Message}";
        }

        return new RawImageInfo(length, table, system, files, problem, unreadable);
    }

    // BOOT*.EFI in \EFI\BOOT first, then shim*.efi in every directory of \EFI.
    private static IEnumerable<(string Path, FatEntry Entry)> BootFileEntries(FatVolume volume)
    {
        if (volume.Find("EFI") is not { IsDirectory: true })
        {
            yield break;
        }

        IReadOnlyList<FatEntry> directories = [.. volume.List("EFI").Where(entry => entry.IsDirectory)];

        foreach (FatEntry directory in directories.Where(entry => string.Equals(entry.Name, "BOOT", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (FatEntry file in volume.List($@"EFI\{directory.Name}").Where(entry => !entry.IsDirectory && IsFallback(entry.Name)))
            {
                yield return ($@"\EFI\{directory.Name}\{file.Name}", file);
            }
        }

        foreach (FatEntry directory in directories)
        {
            foreach (FatEntry file in volume.List($@"EFI\{directory.Name}").Where(entry => !entry.IsDirectory && IsShim(entry.Name)))
            {
                yield return ($@"\EFI\{directory.Name}\{file.Name}", file);
            }
        }
    }

    private static bool IsFallback(string name) =>
        name.StartsWith("BOOT", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".EFI", StringComparison.OrdinalIgnoreCase);

    private static bool IsShim(string name) =>
        name.StartsWith("shim", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".efi", StringComparison.OrdinalIgnoreCase);

    private static byte[] ReadHead(Stream image, long count)
    {
        byte[] head = new byte[count];
        image.Position = 0;

        if (image.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length)
        {
            throw new InvalidGptException(GptLayout.NoTableMessage);
        }

        return head;
    }
}
