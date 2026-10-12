// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// A save's steps on the real disk. Second names and a folder written out are Linux's. Windows needs neither.
internal sealed class ReplacementDisk(AgentLog log) : IReplacementDisk
{
    // Said once: a save comes every few seconds.
    private static int s_toldNoSecondName;

    public async Task WriteNewAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        // Never through a link someone left at the name: the name is new, or the save fails
        FileStream file = new(path, OwnFile.Private(FileMode.CreateNew, FileOptions.WriteThrough));

        await using (file.ConfigureAwait(false))
        {
            await file.WriteAsync(content, cancellationToken).ConfigureAwait(false);
            file.Flush(flushToDisk: true);
        }
    }

    public bool Link(string path, string second)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists(path))
        {
            return false;
        }

        if (LinuxFileNativeMethods.link(path, second) == 0)
        {
            return true;
        }

        string why = Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError());

        if (Interlocked.Exchange(ref s_toldNoSecondName, 1) == 0)
        {
            log.Warning(
                $"The copy of {path} that a save replaces cannot be kept under a second name ({why}), so its bytes are not " +
                "overwritten and stay on the disk until something else is written there.");
        }

        return false;
    }

    public void Rename(string from, string to) => File.Move(from, to, overwrite: true);

    public bool Flush(string directory)
    {
        if (!OperatingSystem.IsLinux())
        {
            return true;
        }

        int folder = LinuxFileNativeMethods.open(directory, LinuxFileNativeMethods.ReadOnlyCloseOnExec, 0);

        if (folder < 0)
        {
            return false;
        }

        try
        {
            return LinuxFileNativeMethods.fsync(folder) == 0;
        }
        finally
        {
            _ = LinuxFileNativeMethods.close(folder);
        }
    }

    public void Remove(string path, bool overwrite)
    {
        if (overwrite)
        {
            Leftovers.Wipe(path, log);
        }
        else
        {
            Leftovers.Delete(path, log);
        }
    }
}
