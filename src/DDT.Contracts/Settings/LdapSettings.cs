// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The ldap section. Its secret is bindPassword.
public sealed record LdapSettings(
    bool Enabled,
    string Host,
    int Port,
    DirectoryTransport Transport,
    string BaseDn,
    string BindDn,
    string UserFilter,
    string ImmutableIdAttribute,
    string DisplayNameAttribute,
    string EmailAttribute,
    bool ResolveNestedGroups,
    // Maps a group's distinguished name to a role.
    IReadOnlyDictionary<string, string> GroupRoleMap,
    TimeSpan Timeout);
