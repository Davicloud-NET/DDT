// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Data;

public sealed class DdtUser : IdentityUser<Guid>
{
    public AccountSource Source { get; set; } = AccountSource.Local;

    public string? DirectoryObjectId { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? LastSignInUtc { get; set; }

    public bool IsDisabled { get; set; }
}
