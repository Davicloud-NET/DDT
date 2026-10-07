// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The free space of the drive a path is on, as Windows counts it for the agent's account.
public sealed class DriveFreeSpace : IFreeSpace
{
    public long AvailableBytes(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? throw new ArgumentException($"{path} is on no drive.", nameof(path));

        return new DriveInfo(root).AvailableFreeSpace;
    }
}
