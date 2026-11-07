// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// The dry run's disks. A file next to the root stands in for each, and stays until the next clean deletes it.
public sealed class FileRawDisks(string root, AgentLog log, Action<SafeFileHandle>? makeSparse = null) : IRawDisks
{
    public static string PathFor(string root, int diskNumber) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Path.TrimEndingDirectorySeparator(root)}-disk{diskNumber}.img");

    public IRawDisk Open(LocalDisk disk)
    {
        ArgumentNullException.ThrowIfNull(disk);

        string path = PathFor(root, disk.Number);
        log.Information($"Dry run: {path} stands in for disk {disk.Number}.");

        return new FileRawDisk(path, disk.SizeBytes, makeSparse);
    }
}
