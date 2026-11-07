// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DDT.Pxe.Tests;

// Keeps every message that is logged, for what a test can see only in the log.
internal sealed class RecordedLog : ILoggerFactory
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName) => new Recorder(_messages);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class Recorder(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Enqueue($"{logLevel} {eventId.Id}: {formatter(state, exception)}");
    }
}
