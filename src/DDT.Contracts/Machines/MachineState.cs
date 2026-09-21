// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

public enum MachineState
{
    Pending,
    Approved,
    Deploying,
    Done,
    Failed,
    Rejected,
    Retired,
}
