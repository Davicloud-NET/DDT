// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Data;

public enum AccountSource
{
    Local,
    Directory,

    // Created by single sign-on. IdentityBootstrap turns one stored as Directory, without a DirectoryObjectId, into this.
    External,
}
