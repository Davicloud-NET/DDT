// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Tokens;

// A token a user made for a script or another system to call the API as them. It is a principal of its own, apart from
// the tokens machines get: it acts for a person, never for a machine. A revoked token keeps its row, so the list and the
// audit log can still name it.
public sealed class ApiToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string Name { get; set; }

    // A role name from DdtRoleNames, the most the token may do.
    public required string Role { get; set; }

    // SHA-256 of the whole secret, as lower case hex. The secret itself is shown once and never stored.
    public required string SecretHash { get; set; }

    public required string Hint { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset ExpiresUtc { get; set; }

    public DateTimeOffset? LastUsedUtc { get; set; }

    public string? LastUsedAddress { get; set; }

    public DateTimeOffset? RevokedUtc { get; set; }

    public Guid? RevokedByUserId { get; set; }

    public string? RevokedByName { get; set; }
}
