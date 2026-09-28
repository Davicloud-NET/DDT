// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Users;

internal enum UserChangeStatus
{
    Done,
    NotFound,
    Invalid,
    Refused,

    // Identity did not save the account, which after validation means it changed meanwhile or the store failed.
    NotSaved,
}
