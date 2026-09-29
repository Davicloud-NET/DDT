// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.Json.Nodes;
using DDT.Core.Configuration;
using DDT.Server.Machines;
using DDT.Server.Security;
using Microsoft.Extensions.Configuration;

namespace DDT.Server.Settings;

// Checks the settings as configuration alone sets them, before the server starts. A configured value that breaks its
// rules stops the start. A problem in a field left to the page, such as a domain whose password is stored there, is
// reported by its section once the stored values are read. The section then fails closed until it's fixed.
public static class ConfiguredSettings
{
    public static IReadOnlyList<string> FindProblems(IConfiguration configuration, bool pxe)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Dictionary<string, object> options = new(StringComparer.Ordinal);

        foreach (SettingsSectionDefinition definition in SettingsDefinitions.All)
        {
            // A section that cannot be read was reported by the key check already.
            try
            {
                JsonObject values = definition.Configured(configuration);
                Dictionary<string, string?> secrets = definition.Secrets.ToDictionary(
                    field => field.Name,
                    field => configuration[definition.ConfigurationKey(field)],
                    StringComparer.Ordinal);

                options[definition.Name] = definition.Read(values, secrets);
            }
            catch (Exception exception) when (exception is InvalidOperationException or JsonException or NotSupportedException or FormatException)
            {
            }
        }

        SettingsContext context = new()
        {
            Machines = options.GetValueOrDefault(SettingsSectionNames.Machines) as MachineOptions ?? new MachineOptions(),
            Proxies = options.GetValueOrDefault(SettingsSectionNames.Proxies) as DdtForwardedHeadersOptions ?? new DdtForwardedHeadersOptions(),
            HttpBootPort = SettingsBootstrap.From(configuration).HttpBootPort,
        };

        List<string> problems = [];

        foreach (SettingsSectionDefinition definition in SettingsDefinitions.All)
        {
            // Only a process that serves netboot needs the PXE values. The PXE keys are checked in every process.
            if (!options.TryGetValue(definition.Name, out object? section) || (definition.Name == SettingsSectionNames.Pxe && !pxe))
            {
                continue;
            }

            problems.AddRange(definition.FindProblems(section, context)
                .Where(problem => IsConfigured(definition, configuration, problem))
                .Select(problem => problem.Describe(definition.ConfigurationPath)));
        }

        return problems;
    }

    // A problem belongs to configuration when configuration sets its field. A collection counts as one field. A problem
    // that isn't about a field of the page, such as HttpBootPort, is checked by its own key.
    private static bool IsConfigured(SettingsSectionDefinition definition, IConfiguration configuration, SettingProblem problem) =>
        definition.FieldOf(problem.Field) is { } field
            ? definition.IsLocked(configuration, field)
            : problem.Field.Length > 0 && configuration.GetSection($"{definition.ConfigurationPath}:{problem.Field}").Exists();
}
