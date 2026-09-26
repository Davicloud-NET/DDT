// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Users;

// Local accounts have a password in DDT. Directory accounts sign in with the directory's password, and External
// accounts through single sign-on.
public enum UserSource
{
    Local,
    Directory,
    External,
}
