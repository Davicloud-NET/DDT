// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;
using DDT.Server.Ldap;
using Microsoft.Extensions.Configuration;

namespace DDT.Server.Settings;

public sealed class LdapSettingsSection() : SettingsSectionDefinition<LdapOptions>(
    SettingsSectionNames.Ldap,
    LdapOptions.SectionName,
    SettingsSectionKind.Live,
    [
        new("Enabled"),
        new("Host", reauthenticate: true),
        new("Port", reauthenticate: true),
        new("Transport", reauthenticate: true),
        new("BaseDn"),
        new("BindDn"),
        new("BindPassword", SettingFieldKind.Secret),
        new("UserFilter"),
        new("ImmutableIdAttribute"),
        new("DisplayNameAttribute"),
        new("EmailAttribute"),
        new("ResolveNestedGroups"),
        new("GroupRoleMap", SettingFieldKind.Collection, reauthenticate: true),
        new("Timeout"),
    ])
{
    // The fields that decide where the bind password goes. A stored password is only kept while these stay the same.
    public static IReadOnlyList<string> Destination { get; } = ["Host", "Port", "Transport"];

    protected override JsonTypeInfo<LdapOptions> TypeInfo => SettingsJsonContext.Default.LdapOptions;

    protected override LdapOptions? Bind(IConfigurationSection section) => section.Get<LdapOptions>();

    protected override JsonNode? BindCollection(IConfigurationSection section, SettingField field) =>
        JsonSerializer.SerializeToNode(section.Get<Dictionary<string, string>>() ?? [], SettingsJsonContext.Default.DictionaryStringString);

    protected override void SetSecret(LdapOptions options, SettingField field, string? value) => options.BindPassword = value ?? string.Empty;

    protected override string? GetSecret(LdapOptions options, SettingField field) => options.BindPassword;

    protected override void Normalize(LdapOptions options) =>
        options.GroupRoleMap = SettingsCollections.IgnoringCase(options.GroupRoleMap);

    protected override IReadOnlyList<SettingProblem> FindProblems(LdapOptions options, SettingsContext context) =>
        LdapOptionsValidation.FindProblems(options);

    protected override IReadOnlyList<SettingWarning> FindWarnings(LdapOptions options, LdapOptions? current, SettingsContext context) =>
        LdapOptionsValidation.FindWarnings(options, current);
}
