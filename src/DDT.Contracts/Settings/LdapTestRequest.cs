// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The values and secrets as the form holds them, not yet saved.
public sealed record LdapTestRequest(
    LdapSettings Values,
    IReadOnlyDictionary<string, SecretUpdate>? Secrets,
    // With Password, signs that user in without a session, to show their groups and the roles they would get.
    string? UserName,
    string? Password);
