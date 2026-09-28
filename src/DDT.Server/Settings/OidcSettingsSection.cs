// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;
using DDT.Server.Authentication;
using Microsoft.Extensions.Configuration;

namespace DDT.Server.Settings;

public sealed class OidcSettingsSection() : SettingsSectionDefinition<OidcOptions>(
    SettingsSectionNames.Oidc,
    OidcOptions.SectionName,
    SettingsSectionKind.Restart,
    [
        new("Enabled"),
        new("Authority", reauthenticate: true),
        new("ClientId"),
        new("ClientSecret", SettingFieldKind.Secret),
        new("DisplayName"),
        new("Scopes", SettingFieldKind.Collection),
        new("AutoProvision", reauthenticate: true),
        new("AutoProvisionRole", reauthenticate: true),
        new("GroupsClaim", reauthenticate: true),
        new("GroupRoleMap", SettingFieldKind.Collection, reauthenticate: true),
    ])
{
    // The field that decides where the client secret goes. A stored secret is only kept while it stays the same.
    public static IReadOnlyList<string> Destination { get; } = ["Authority"];

    protected override JsonTypeInfo<OidcOptions> TypeInfo => SettingsJsonContext.Default.OidcOptions;

    protected override OidcOptions? Bind(IConfigurationSection section) => section.Get<OidcOptions>();

    protected override JsonNode? BindCollection(IConfigurationSection section, SettingField field) => field.Path switch
    {
        "GroupRoleMap" => JsonSerializer.SerializeToNode(section.Get<Dictionary<string, string>>() ?? [], SettingsJsonContext.Default.DictionaryStringString),
        _ => JsonSerializer.SerializeToNode(section.Get<List<string>>() ?? [], SettingsJsonContext.Default.ListString),
    };

    protected override void SetSecret(OidcOptions options, SettingField field, string? value) => options.ClientSecret = value ?? string.Empty;

    protected override string? GetSecret(OidcOptions options, SettingField field) => options.ClientSecret;

    protected override void Normalize(OidcOptions options) =>
        options.GroupRoleMap = SettingsCollections.IgnoringCase(options.GroupRoleMap);

    protected override IReadOnlyList<SettingProblem> FindProblems(OidcOptions options, SettingsContext context) =>
        OidcOptionsValidation.FindProblems(options);

    protected override IReadOnlyList<SettingWarning> FindWarnings(OidcOptions options, OidcOptions? current, SettingsContext context) =>
        options.AutoProvision
        && options.GroupRoleMap.Count == 0
        && string.Equals(options.AutoProvisionRole, DdtRoleNames.Operator, StringComparison.OrdinalIgnoreCase)
            ?
            [
                new("AutoProvisionRole", ServerMessages.SettingsOidcOperatorRole.With(), SettingWarningCodes.OidcOperatorRole),
            ]
            : [];
}
