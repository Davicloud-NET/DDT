// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Settings;

// Only Logging:LogLevel belongs to DDT. Logging providers add their own subsections, and those stay in configuration.
public sealed class LoggingSettingsSection() : SettingsSectionDefinition<LoggingOptions>(
    SettingsSectionNames.Logging,
    LoggingOptions.SectionName,
    SettingsSectionKind.Live,
    [
        new("LogLevel", SettingFieldKind.Collection),
    ])
{
    protected override JsonTypeInfo<LoggingOptions> TypeInfo => SettingsJsonContext.Default.LoggingOptions;

    protected override LoggingOptions? Bind(IConfigurationSection section) => section.Get<LoggingOptions>();

    protected override JsonNode? BindCollection(IConfigurationSection section, SettingField field) =>
        JsonSerializer.SerializeToNode(section.Get<Dictionary<string, string>>() ?? [], SettingsJsonContext.Default.DictionaryStringString);

    protected override void Normalize(LoggingOptions options) =>
        options.LogLevel = SettingsCollections.IgnoringCase(options.LogLevel);

    protected override IReadOnlyList<SettingProblem> FindProblems(LoggingOptions options, SettingsContext context) =>
    [
        .. options.LogLevel
            .Where(level => TryParse(level.Value) is null)
            .Select(level => new SettingProblem($"LogLevel:{level.Key}", ServerMessages.SettingsLoggingLevelUnknown.With("value", level.Value ?? string.Empty))),
    ];

    // Parses by name only. Enum.TryParse also takes a number, and "7" isn't a level.
    public static LogLevel? TryParse(string? value) =>
        Enum.GetNames<LogLevel>().FirstOrDefault(name => string.Equals(name, value?.Trim(), StringComparison.OrdinalIgnoreCase)) is { } name
            ? Enum.Parse<LogLevel>(name)
            : null;
}
