// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Tests;

// Keeps every message a host logs, with its event id and level, for tests that can only see behaviour in the log.
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<(int EventId, LogLevel Level, string Message)> _entries = new();

    public IEnumerable<string> Messages => _entries.Select(entry => entry.Message);

    public bool Logged(int eventId, LogLevel level) => _entries.Any(entry => entry.EventId == eventId && entry.Level == level);

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(_entries);

    public async Task WaitForAsync(string text, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        while (!Messages.Any(message => message.Contains(text, StringComparison.Ordinal)))
        {
            await Task.Delay(50, deadline.Token);
        }
    }

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(ConcurrentQueue<(int EventId, LogLevel Level, string Message)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            entries.Enqueue((eventId.Id, logLevel, formatter(state, exception)));
        }
    }
}
