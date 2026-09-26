// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Data;

// External accounts were created by single sign-on. Before M6.5 they were stored as Directory accounts, which
// IdentityBootstrap corrects at start: a directory account always has a DirectoryObjectId, and they never do.
public enum AccountSource
{
    Local,
    Directory,
    External,
}
