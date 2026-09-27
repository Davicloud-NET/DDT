// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace DDT.Server.Settings;

internal static class ConfigurationSources
{
    // Where a key, or a section below it, comes from: the last source that sets it wins, as it does for the value. Null
    // when no source sets it.
    public static string? Describe(IConfiguration configuration, string key)
    {
        if (configuration is not IConfigurationRoot root)
        {
            return null;
        }

        foreach (IConfigurationProvider provider in root.Providers.Reverse())
        {
            if (provider.TryGet(key, out _) || provider.GetChildKeys([], key).Any())
            {
                return provider switch
                {
                    EnvironmentVariablesConfigurationProvider => "environment variable",
                    CommandLineConfigurationProvider => "command line",
                    FileConfigurationProvider file => Path.GetFileName(file.Source.Path) ?? "a configuration file",
                    _ => "configuration",
                };
            }
        }

        return null;
    }
}
