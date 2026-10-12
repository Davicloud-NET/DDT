// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// How a dry run's fake machine starts once its run was handed over to the installed Linux.
public enum DryRunNetboot
{
    // From its disk, as a machine does whose firmware keeps the boot order.
    Never,

    // From the network once more when the installed Linux has taken the run over, and then from its disk.
    OnceInLinux,

    // From the network once more before the installed Linux has taken the run over, which it does at the start after.
    OnceBeforeLinux,

    // From the network every time, with an installed Linux that never runs the agent's script.
    Always,
}
