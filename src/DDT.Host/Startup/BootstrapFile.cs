// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Ini;

namespace DDT.Host.Startup;

// ddt.ini, which the MSI writes. Overrides appsettings.json; environment variables and the command line override it.
public static class BootstrapFile
{
    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DDT", "ddt.ini");

    public static void Add(IConfigurationBuilder configuration, string path)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrEmpty(path);

        IniConfigurationSource source = new() { Path = path, Optional = true };
        source.ResolveFileProvider();

        // Before the unprefixed environment variables.. The ASPNETCORE_ and DOTNET_ ones are host settings
        int index = configuration.Sources.Count;

        for (int i = 0; i < configuration.Sources.Count; i++)
        {
            if (configuration.Sources[i] is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
            {
                index = i;
            }
        }

        configuration.Sources.Insert(index, source);
    }
}
