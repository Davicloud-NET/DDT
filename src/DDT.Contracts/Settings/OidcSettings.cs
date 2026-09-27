// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The section oidc. Secrets: clientSecret. GroupRoleMap maps a value of the GroupsClaim claim to a role; while it has
// entries, it decides the role of the accounts single sign-on made, and AutoProvisionRole is not used.
public sealed record OidcSettings(
    bool Enabled,
    string Authority,
    string ClientId,
    string DisplayName,
    IReadOnlyList<string> Scopes,
    bool AutoProvision,
    string AutoProvisionRole,
    string GroupsClaim,
    IReadOnlyDictionary<string, string> GroupRoleMap);
