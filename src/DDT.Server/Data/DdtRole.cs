// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Data;

public sealed class DdtRole : IdentityRole<Guid>
{
    public DdtRole()
    {
    }

    public DdtRole(string roleName) : base(roleName)
    {
    }
}
