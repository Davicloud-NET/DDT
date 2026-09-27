// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Settings;

// The levels of the logging section. The defaults used to be in appsettings.json, where a key locks its field.
public sealed class LoggingOptions
{
    public const string SectionName = "Logging";

    public const string DefaultCategory = "Default";

    public Dictionary<string, string> LogLevel { get; set; } = Defaults();

    public static Dictionary<string, string> Defaults() => new(StringComparer.OrdinalIgnoreCase)
    {
        [DefaultCategory] = "Information",
        ["Microsoft.AspNetCore"] = "Warning",
        ["Microsoft.EntityFrameworkCore.Database.Command"] = "Warning",
        ["Microsoft.EntityFrameworkCore.Infrastructure"] = "Warning",
    };
}
