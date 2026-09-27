// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

public enum ConsoleStepState
{
    Pending,
    Running,
    Done,

    // Its conditions did not hold.
    Skipped,
    Failed,
}
