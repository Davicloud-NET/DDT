// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Accounts;

// An account that steps use. It's limited to where it may be used: Domain for a domain join, Hosts (JSON) for
// shares, RunAs for scripts. AccountProtector encrypts ProtectedPassword for this account. It's null if none is set.
public sealed class Account
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    // The trimmed name in upper case. It's unique, so two account names can't differ only in case.
    public required string NormalizedName { get; set; }

    public required string UserName { get; set; }

    public string? ProtectedPassword { get; set; }

    public string? Domain { get; set; }

    public string Hosts { get; set; } = "[]";

    public bool RunAs { get; set; }

    public DateTimeOffset? PasswordUpdatedUtc { get; set; }

    // Every save checks it and raises it. An editor who saves over a newer save is told, instead of overwriting it.
    public long Revision { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string? UpdatedByName { get; set; }
}
