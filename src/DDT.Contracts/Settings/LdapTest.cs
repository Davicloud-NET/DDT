// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The values and secrets as the form holds them, not yet saved. UserName and Password also sign that user in, without a
// session, to show their groups and the roles they would get.
public sealed record LdapTestRequest(
    LdapSettings Values,
    IReadOnlyDictionary<string, SecretUpdate>? Secrets,
    string? UserName,
    string? Password);

// Bound: the bind with BindDn succeeded. UserFound and PasswordAccepted are null when no user was named. Role is the one
// the groups would give, the highest of those GroupRoleMap maps them to, and null for none. Proof is set when the user
// was the administrator testing, signed in with these values and kept the Administrator role: a directory administrator
// sends it as X-DDT-Directory-Proof with a save of exactly these values, within 5 minutes.
public sealed record LdapTestResult(
    bool Bound,
    bool? UserFound,
    bool? PasswordAccepted,
    IReadOnlyList<string> Groups,
    string? Role,
    string Message,
    string? Proof);
