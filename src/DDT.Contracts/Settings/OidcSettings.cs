// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The oidc section. Its secret is clientSecret.
public sealed record OidcSettings(
    bool Enabled,
    string Authority,
    string ClientId,
    string DisplayName,
    IReadOnlyList<string> Scopes,
    bool AutoProvision,
    string AutoProvisionRole,
    string GroupsClaim,
    // Maps a value of the GroupsClaim claim to a role. While it has entries, it decides the role of accounts that
    // single sign-on created, and AutoProvisionRole isn't used.
    IReadOnlyDictionary<string, string> GroupRoleMap);
