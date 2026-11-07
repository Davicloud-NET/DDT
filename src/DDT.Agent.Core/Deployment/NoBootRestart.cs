// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The boot environment's due restart as an installed Linux has it: none. The restarts a run asks for there are the
// installed phase's own, with a marker of their own.
public sealed class NoBootRestart : IBootRestartMarker
{
    public RestartInto? Due => null;

    public void Set(RestartInto into)
    {
    }

    public void Clear()
    {
    }
}
