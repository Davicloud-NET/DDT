// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;
using DDT.Pxe;
using Microsoft.Extensions.Configuration;

namespace DDT.Server.Settings;

// HttpBootPort and BootDirectory stay in configuration, so they are no fields: what applies takes them from there.
public sealed class PxeSettingsSection() : SettingsSectionDefinition<PxeOptions>(
    SettingsSectionNames.Pxe,
    PxeOptions.SectionName,
    SettingsSectionKind.Restart,
    [
        new("Interfaces"),
        new("EnableProxyDhcp"),
        new("EnableTftp"),
        new("TftpSinglePort"),
        new("TftpMaxWindowSize"),
        new("MaxConcurrentTftpTransfers"),
        new("AuthorisedRelayAgents"),
        new(
            "BootTargets",
            SettingFieldKind.Collection,
            entryMembers: [.. SettingsJsonContext.Default.BootTargetOptions.Properties.Select(member => member.Name)]),
    ])
{
    protected override JsonTypeInfo<PxeOptions> TypeInfo => SettingsJsonContext.Default.PxeOptions;

    protected override PxeOptions? Bind(IConfigurationSection section) => section.Get<PxeOptions>();

    protected override JsonNode? BindCollection(IConfigurationSection section, SettingField field) =>
        JsonSerializer.SerializeToNode(
            section.Get<Dictionary<string, BootTargetOptions>>() ?? [],
            SettingsJsonContext.Default.DictionaryStringBootTargetOptions);

    protected override void Normalize(PxeOptions options) =>
        options.BootTargets = SettingsCollections.IgnoringCase(options.BootTargets);

    protected override IReadOnlyList<SettingProblem> FindProblems(PxeOptions options, SettingsContext context) => PxeSetup.FindProblems(options);

    // DDT serves HTTP boot files only on its boot port, below /boot/. Another server may serve the file, so this only
    // asks.
    protected override IReadOnlyList<SettingWarning> FindWarnings(PxeOptions options, PxeOptions? current, SettingsContext context)
    {
        List<SettingWarning> warnings = [];

        foreach ((string architecture, BootTargetOptions target) in options.BootTargets)
        {
            if (!string.Equals(target.Method?.Trim(), "Http", StringComparison.OrdinalIgnoreCase)
                || !Uri.TryCreate(target.BootFile?.Trim(), UriKind.Absolute, out Uri? url))
            {
                continue;
            }

            if (url.Port != context.HttpBootPort || !url.AbsolutePath.StartsWith("/boot/", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(new(
                    $"BootTargets:{architecture}:BootFile",
                    ServerMessages.SettingsPxeBootUrl.With("url", url.ToString(), "port", context.HttpBootPort),
                    SettingWarningCodes.PxeBootUrl));
            }
        }

        return warnings;
    }
}
