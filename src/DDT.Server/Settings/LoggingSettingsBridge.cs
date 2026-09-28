// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace DDT.Server.Settings;

// The logger factory applies new filter rules when its options monitor reports a change. The change token makes it
// report one at every publish. These rules come after configuration's, and a later rule wins for the same category.
public sealed class LoggingSettingsBridge(DdtSettings settings) : IConfigureOptions<LoggerFilterOptions>, IOptionsChangeTokenSource<LoggerFilterOptions>
{
    public string? Name => Options.DefaultName;

    public IChangeToken GetChangeToken() => settings.GetChangeToken();

    public void Configure(LoggerFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach ((string category, LogLevel level) in settings.Current.LogLevels)
        {
            string? name = string.Equals(category, LoggingOptions.DefaultCategory, StringComparison.OrdinalIgnoreCase) ? null : category;
            options.Rules.Add(new LoggerFilterRule(null, name, level, null));
        }
    }
}
