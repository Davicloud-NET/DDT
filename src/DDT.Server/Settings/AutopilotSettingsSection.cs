// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization.Metadata;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;
using DDT.Server.Autopilot;
using Microsoft.Extensions.Configuration;

namespace DDT.Server.Settings;

public sealed class AutopilotSettingsSection() : SettingsSectionDefinition<AutopilotOptions>(
    SettingsSectionNames.Autopilot,
    AutopilotOptions.SectionName,
    SettingsSectionKind.Live,
    [
        new("TenantId"),
        new("ClientId"),
        new("ClientSecret", SettingFieldKind.Secret),
        new("DefaultGroupTag"),
    ])
{
    // The fields that decide where the client secret goes. A stored secret is only kept while these stay the same.
    public static IReadOnlyList<string> Destination { get; } = ["TenantId", "ClientId"];

    protected override JsonTypeInfo<AutopilotOptions> TypeInfo => SettingsJsonContext.Default.AutopilotOptions;

    protected override AutopilotOptions? Bind(IConfigurationSection section) => section.Get<AutopilotOptions>();

    protected override void SetSecret(AutopilotOptions options, SettingField field, string? value) => options.ClientSecret = value ?? string.Empty;

    protected override string? GetSecret(AutopilotOptions options, SettingField field) => options.ClientSecret;

    protected override IReadOnlyList<SettingProblem> FindProblems(AutopilotOptions options, SettingsContext context)
    {
        List<SettingProblem> problems = [];
        bool hasTenant = options.TenantId.Length > 0;
        bool hasClient = options.ClientId.Length > 0;

        if (hasTenant && !IsTenant(options.TenantId))
        {
            problems.Add(new(nameof(AutopilotOptions.TenantId), ServerMessages.SettingsAutopilotTenantId.With()));
        }

        if (hasClient && !Guid.TryParse(options.ClientId, out _))
        {
            problems.Add(new(nameof(AutopilotOptions.ClientId), ServerMessages.SettingsAutopilotClientId.With()));
        }

        if (hasTenant != hasClient)
        {
            problems.Add(new(hasTenant ? nameof(AutopilotOptions.ClientId) : nameof(AutopilotOptions.TenantId), ServerMessages.SettingsAutopilotBoth.With()));
        }

        if (!AutopilotGroupTags.IsValid(options.DefaultGroupTag))
        {
            problems.Add(new(
                nameof(AutopilotOptions.DefaultGroupTag),
                ServerMessages.SettingsAutopilotGroupTag.With("max", RegisterAutopilotStep.MaxGroupTagLength)));
        }

        return problems;
    }

    // The tenant goes into the token endpoint's path, so only a GUID or a host name is taken.
    private static bool IsTenant(string tenant) =>
        Guid.TryParse(tenant, out _) || (Uri.CheckHostName(tenant) == UriHostNameType.Dns && tenant.Contains('.', StringComparison.Ordinal));
}
