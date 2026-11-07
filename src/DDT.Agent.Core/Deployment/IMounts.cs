// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The kernel's mount calls as the agent makes them in the installed Linux. Each returns 0 or the error number.
public interface IMounts
{
    // options are the file system's own and may hold a password, so no implementation logs them or throws them.
    int Mount(string source, string target, string fileSystem, string options);

    // detach takes the mount away at once and lets the kernel close it when its last file is closed.
    int Unmount(string target, bool detach);
}
