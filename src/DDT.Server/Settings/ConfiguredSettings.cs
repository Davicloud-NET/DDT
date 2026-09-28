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

// The settings as configuration alone sets them, checked before the server starts: a configured value that fails its
// rules stops the start. A problem of a field left to the page, such as a domain whose password is stored there, is the
// section's once the stored values are read, and the section fails closed until it is fixed.
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
            // Its values matter only to a process that serves netboot. Its keys are checked in every process.
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

    // A problem belongs to configuration when configuration sets its field: a collection as a whole, anything else by its
    // own key, such as HttpBootPort, which is no field of the page.
    private static bool IsConfigured(SettingsSectionDefinition definition, IConfiguration configuration, SettingProblem problem) =>
        definition.FieldOf(problem.Field) is { } field
            ? definition.IsLocked(configuration, field)
            : problem.Field.Length > 0 && configuration.GetSection($"{definition.ConfigurationPath}:{problem.Field}").Exists();
}
