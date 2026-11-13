// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Applications;

// Stored in a step's checkpoint by name, so the names never change.
public enum ApplicationState
{
    Pending,

    // Its installer was started and has not ended yet, as far as the checkpoint knows.
    Started,

    Installed,
    AlreadyThere,
    Failed,

    // An application it depends on failed or was skipped.
    Skipped,
}
