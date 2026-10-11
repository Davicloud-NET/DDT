// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.LinuxPhase;

namespace DDT.Agent.Tests;

// Folders that stand in for mounted cloud-init seeds. Without a folder, no seed can be mounted.
internal sealed class FolderSeeds(params string[] folders) : IMountedSeeds
{
    public int Visits { get; private set; }

    public int Each(Action<string> visit)
    {
        foreach (string folder in folders)
        {
            Visits++;
            visit(folder);
        }

        return folders.Length;
    }
}
