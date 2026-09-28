// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Accounts;

public enum AccountPurpose
{
    // A script runs as the account, in Windows.
    RunAs,

    // A Join the domain step joins with it.
    Join,

    // DDT connects a share with it while the step runs.
    Share,
}
