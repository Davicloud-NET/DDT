// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Tests;

// Keeps the event id and level of every entry the host logs.
public sealed class RecordingLoggerProvider : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<(int EventId, LogLevel Level)> _entries = new();

    public bool Logged(int eventId, LogLevel level) => _entries.Contains((eventId, level));

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _entries.Enqueue((eventId.Id, logLevel));

    public void Dispose()
    {
    }
}
