// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Server.Ldap;

public sealed class LdapOptions
{
    public const string SectionName = "DDT:Ldap";

    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 636;

    public LdapTransport Transport { get; set; } = LdapTransport.Ldaps;

    public string BaseDn { get; set; } = string.Empty;

    public string BindDn { get; set; } = string.Empty;

    // A secret. It's stored encrypted, never in the section's values.
    [JsonIgnore]
    public string BindPassword { get; set; } = string.Empty;

    public string UserFilter { get; set; } = "(&(objectClass=user)(sAMAccountName={0}))";

    public string ImmutableIdAttribute { get; set; } = "objectGUID";

    public string DisplayNameAttribute { get; set; } = "displayName";

    public string EmailAttribute { get; set; } = "mail";

    public bool ResolveNestedGroups { get; set; } = true;

    // Maps a group's distinguished name to a role. Distinguished names are compared ignoring case, like the directory
    // does. While the map is empty, directory groups don't decide any role, and administrators set the roles.
    public Dictionary<string, string> GroupRoleMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
