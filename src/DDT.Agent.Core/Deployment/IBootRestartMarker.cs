// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// A restart the boot environment still owes the run, kept where the restart itself clears it.
public interface IBootRestartMarker
{
    // Where the due restart leads, or null when none is due.
    RestartInto? Due { get; }

    void Set(RestartInto into);

    // The run gave up the restart.
    void Clear();
}
