// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Users;

// Where an account's role comes from. Manual: an administrator set it. DirectoryGroups and SingleSignOnGroups: the
// account's groups decide it at each sign-in, so it cannot be changed in DDT. Provisioned: DDT gave it to the account
// when single sign-on created it, and no administrator has changed it since.
public enum RoleSource
{
    Manual,
    DirectoryGroups,
    SingleSignOnGroups,
    Provisioned,
}
