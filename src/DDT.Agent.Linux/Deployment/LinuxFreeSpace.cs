// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The free space of the file system a path is on, as Linux counts it for the agent.
public sealed class LinuxFreeSpace : IFreeSpace
{
    // The path may not exist yet, as a folder that is about to be made. Its nearest folder that does is asked.
    public long AvailableBytes(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string? existing = Path.GetFullPath(path);

        while (existing is not null && !Directory.Exists(existing) && !File.Exists(existing))
        {
            existing = Path.GetDirectoryName(existing);
        }

        return new DriveInfo(existing ?? throw new ArgumentException($"{path} is on no file system.", nameof(path))).AvailableFreeSpace;
    }
}
