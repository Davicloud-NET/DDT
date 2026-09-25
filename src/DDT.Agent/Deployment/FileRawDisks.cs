// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The dry run's disks: a file beside the dry run's root for each, as large as the disk it stands in for. It outlasts the
// run, whose root goes when the run ends, so the disk can be looked at afterwards; the next clean deletes it.
public sealed class FileRawDisks(string root, AgentLog log) : IRawDisks
{
    public static string PathFor(string root, int diskNumber) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Path.TrimEndingDirectorySeparator(root)}-disk{diskNumber}.img");

    public IRawDisk Open(LocalDisk disk)
    {
        ArgumentNullException.ThrowIfNull(disk);

        string path = PathFor(root, disk.Number);
        log.Information($"Dry run: {path} stands in for disk {disk.Number}.");

        return new FileRawDisk(path, disk.SizeBytes);
    }
}
