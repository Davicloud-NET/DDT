// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Disks;

namespace DDT.Core.CloudInit;

// A cloud-init seed's volume as a disk holds it: every file by its path, and the volume's serial number. A hand-over
// reads it, so what a step changed on the seed after it was written goes onto the volume it builds.
public sealed record SeedVolume(IReadOnlyList<SeedFile> Files, uint SerialNumber)
{
    // No seed needs folders this deep, and a damaged volume could nest them without end.
    private const int MaxDepth = 8;

    // Reads a whole partition. Throws InvalidDataException with the reason when it is no FAT volume labelled CIDATA,
    // or holds what CloudInitSeed.Build could not write again.
    public static SeedVolume Read(byte[] volume)
    {
        ArgumentNullException.ThrowIfNull(volume);

        FatVolume fat = FatVolume.Open(new MemoryStream(volume, writable: false), 0, volume.Length);

        if (!string.Equals(fat.Label, CloudInitSeed.Label, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(fat.Label is null ? "The volume has no label." : $"The volume is labelled {fat.Label}.");
        }

        List<SeedFile> files = [];
        long room = volume.Length;
        Collect(fat, "", 0, files, ref room);

        return new SeedVolume(files, fat.SerialNumber);
    }

    // What differs from the files a seed step wrote, in words for the log. Null when nothing does.
    public string? ChangesSince(IReadOnlyList<SeedFile> written)
    {
        ArgumentNullException.ThrowIfNull(written);

        List<string> changes = [];

        foreach (SeedFile file in Files)
        {
            SeedFile? before = written.FirstOrDefault(other => SamePath(other.Path, file.Path));

            if (before is null)
            {
                changes.Add($"{file.Path} was added");
            }
            else if (!before.Content.AsSpan().SequenceEqual(file.Content))
            {
                changes.Add($"{file.Path} was changed");
            }
        }

        changes.AddRange(written.Where(file => !Files.Any(other => SamePath(other.Path, file.Path))).Select(file => $"{file.Path} was removed"));

        return changes.Count == 0 ? null : string.Join(", ", changes);
    }

    // FAT names ignore case.
    public static bool SamePath(string one, string other) => string.Equals(one, other, StringComparison.OrdinalIgnoreCase);

    // room is what the volume could hold at most, so cross-linked files cannot fill the memory.
    private static void Collect(FatVolume fat, string folder, int depth, List<SeedFile> files, ref long room)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidDataException($"The volume's folders are nested more than {MaxDepth} deep.");
        }

        IReadOnlyList<FatEntry> entries = fat.List(folder);

        // The builder makes a folder for the files in it, so an empty one would be lost.
        if (folder.Length > 0 && entries.Count == 0)
        {
            throw new InvalidDataException($"The volume holds the empty folder {folder}, which cannot be kept.");
        }

        foreach (FatEntry entry in entries)
        {
            string path = folder.Length == 0 ? entry.Name : $"{folder}/{entry.Name}";

            if (entry.IsDirectory)
            {
                Collect(fat, path, depth + 1, files, ref room);

                continue;
            }

            if (entry.Size > room)
            {
                throw new InvalidDataException("The volume's files are larger than the volume itself.");
            }

            room -= entry.Size;
            files.Add(new SeedFile(path, fat.ReadFile(entry, (int)entry.Size)));
        }
    }
}
