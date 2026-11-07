// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

// The mask that takes modes off every file this process and the processes it starts make. The unit gives the agent
// one that leaves a new file to root alone, and a script expects a root shell's.
public static partial class FileModeMask
{
    // Sets the mask and returns the one before.
    public static int Set(int mask) => (int)umask((uint)mask);

    [LibraryImport("libc")]
    private static partial uint umask(uint mask);
}
