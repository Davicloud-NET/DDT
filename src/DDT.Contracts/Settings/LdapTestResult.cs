// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Contracts.Settings;

public sealed record LdapTestResult(
    // The bind with BindDn succeeded.
    bool Bound,
    // Null, like PasswordAccepted, when no user was named.
    bool? UserFound,
    bool? PasswordAccepted,
    IReadOnlyList<string> Groups,
    // The role the groups would give, which is the highest role GroupRoleMap maps them to. Null for none.
    string? Role,
    // In English. Text holds the same sentence as a code with its values, so a client can show it in the person's
    // language.
    string Message,
    // Set when the administrator running the test signed in with these values and kept the Administrator role. A
    // directory administrator sends it as X-DDT-Directory-Proof when saving exactly these values, within 5 minutes.
    string? Proof,
    ServerMessage? Text = null);
