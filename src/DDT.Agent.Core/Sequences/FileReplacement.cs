// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// Replaces a file that holds a secret through a temporary file written through to disk, so a power loss leaves the old
// file or the new one, never part of one. On Linux the file it replaces is overwritten before its blocks are free.
internal static class FileReplacement
{
    public const string Temporary = ".tmp";

    // The replaced file's second name, which it keeps until it is overwritten.
    public const string Replaced = ".old";

    public static async Task ReplaceAsync(string path, byte[] content, AgentLog log, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Sweep(path, log);
        string temporary = path + Temporary;

        FileStream file = new(temporary, OwnFile.Private(FileMode.Create, FileOptions.WriteThrough));

        await using (file.ConfigureAwait(false))
        {
            await file.WriteAsync(content, cancellationToken).ConfigureAwait(false);
            file.Flush(flushToDisk: true);
        }

        string? replaced = KeepReplaced(path);
        File.Move(temporary, path, overwrite: true);

        if (replaced is not null)
        {
            Leftovers.Wipe(replaced, log);
        }
    }

    // What a save that was cut off left beside the file. A second name of the file itself is only taken away.
    public static void Sweep(string path, AgentLog log)
    {
        Leftovers.Wipe(path + Temporary, log);
        Leftovers.Wipe(path + Replaced, log);
    }

    // Helps where the file system writes in place, as ext4 and xfs do, not where it copies on write, as btrfs does.
    private static string? KeepReplaced(string path)
    {
        string replaced = path + Replaced;

        return OperatingSystem.IsLinux() && File.Exists(path) && LinuxFileNativeMethods.link(path, replaced) == 0 ? replaced : null;
    }
}
