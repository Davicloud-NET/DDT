// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Accounts;

// An account steps use, bound to where it may go: Domain for a join, Hosts (JSON) for shares, RunAs for scripts.
// ProtectedPassword is encrypted for this account by AccountProtector; null when none is set.
public sealed class Account
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    // The trimmed name in upper case, unique, so two accounts cannot differ only in case.
    public required string NormalizedName { get; set; }

    public required string UserName { get; set; }

    public string? ProtectedPassword { get; set; }

    public string? Domain { get; set; }

    public string Hosts { get; set; } = "[]";

    public bool RunAs { get; set; }

    public DateTimeOffset? PasswordUpdatedUtc { get; set; }

    // Raised by every save and checked on it, so an editor saving over a newer save is told instead.
    public long Revision { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string? UpdatedByName { get; set; }
}
