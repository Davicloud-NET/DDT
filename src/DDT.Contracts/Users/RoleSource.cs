// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Users;

// Where an account's role comes from.
public enum RoleSource
{
    // An administrator set it.
    Manual,
    // This and SingleSignOnGroups: the account's groups decide it at each sign-in, so it cannot be changed in DDT.
    DirectoryGroups,
    SingleSignOnGroups,
    // DDT gave it when single sign-on created the account, and no administrator has changed it since.
    Provisioned,
}
