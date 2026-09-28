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
    // The one the groups would give, the highest GroupRoleMap maps them to; null for none.
    string? Role,
    // English; Text is the same sentence as a code with its values, for a client in the person's language.
    string Message,
    // Set when the administrator testing signed in with these values and kept the Administrator role. A directory
    // administrator sends it as X-DDT-Directory-Proof with a save of exactly these values, within 5 minutes.
    string? Proof,
    ServerMessage? Text = null);
