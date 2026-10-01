// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;

namespace DDT.Host.Logging;

// One entry: the local time with its offset, the level, the category with the event's number, the message, and the
// exception on the lines after it.
internal sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    // The Log levels settings filter before this is asked.
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!IsEnabled(logLevel))
        {
            return;
        }

        DateTimeOffset now = provider.Now;
        StringBuilder entry = new();
        entry.Append(CultureInfo.InvariantCulture, $"{now:yyyy-MM-dd HH:mm:ss.fff zzz} {Name(logLevel)} {category}[{eventId.Id}] ");
        entry.AppendLine(formatter(state, exception));

        if (exception is not null)
        {
            entry.AppendLine(exception.ToString());
        }

        provider.Write(now, entry.ToString());
    }

    // As the console logger names them
    private static string Name(LogLevel level) => level switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        _ => "crit",
    };
}
