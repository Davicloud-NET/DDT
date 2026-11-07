// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// Replaces a file that holds a secret through a temporary file written through to disk, so a power loss leaves the old
// file or the new one, never part of one. On Linux the file it replaces is overwritten once the new one has its name
// on the disk, and not before.
internal static class FileReplacement
{
    public const string Temporary = ".tmp";

    // The replaced file's second name, which it keeps until it is overwritten.
    public const string Replaced = ".old";

    public static Task ReplaceAsync(string path, byte[] content, AgentLog log, CancellationToken cancellationToken) =>
        ReplaceAsync(path, content, new ReplacementDisk(log), cancellationToken);

    // The folder is written out before the old bytes go. Until then the disk's own copy of the folder may still name
    // them as the file, and a power loss would leave the file overwritten.
    internal static async Task ReplaceAsync(string path, byte[] content, IReplacementDisk disk, CancellationToken cancellationToken)
    {
        Sweep(path, disk);
        await disk.WriteNewAsync(path + Temporary, content, cancellationToken).ConfigureAwait(false);
        bool kept = disk.Link(path, path + Replaced);
        disk.Rename(path + Temporary, path);
        bool durable = disk.Flush(DirectoryOf(path));

        if (kept)
        {
            disk.Remove(path + Replaced, overwrite: durable);
        }
    }

    // What a save that was cut off left beside the file. A second name of the file itself is only taken away.
    public static void Sweep(string path, AgentLog log) => Sweep(path, new ReplacementDisk(log));

    // For the agent's start, while nothing saves. Where a save that was cut off left a file that cannot be read beside
    // a whole temporary copy, the copy takes its place. isWhole says whether bytes are such a file's.
    public static void Recover(string path, Func<byte[], bool> isWhole, AgentLog log)
    {
        ArgumentNullException.ThrowIfNull(isWhole);

        ReplacementDisk disk = new(log);

        if (Read(path, log) is { } live && !isWhole(live) && Read(path + Temporary, log) is { } saved && isWhole(saved))
        {
            disk.Rename(path + Temporary, path);
            log.Warning($"{path} could not be read after a save that was cut off. The copy that save had written whole is in its place now.");
        }

        Sweep(path, disk);
    }

    // As in a save: the old copy is only overwritten once the folder that names the new one is on the disk.
    private static void Sweep(string path, IReplacementDisk disk)
    {
        bool durable = disk.Flush(DirectoryOf(path));
        disk.Remove(path + Temporary, overwrite: true);
        disk.Remove(path + Replaced, overwrite: durable);
    }

    private static string DirectoryOf(string path) => Path.GetDirectoryName(path) is { Length: > 0 } directory ? directory : ".";

    private static byte[]? Read(string path, AgentLog log)
    {
        try
        {
            return OwnFile.Read(path, Leftovers.MaxWipedBytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"{path} could not be read ({exception.Message}).");

            return null;
        }
    }
}
