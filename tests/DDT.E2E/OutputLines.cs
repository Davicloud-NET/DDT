// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;

namespace DDT.E2E;

// What a started process writes to its standard output and error, kept in memory for the checks and in a file for
// whoever reads up on a failed run.
internal sealed class OutputLines : IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<string> _lines = [];
    private readonly StreamWriter _file;
    private bool _disposed;

    public OutputLines(Process process, string logPath)
    {
        ArgumentNullException.ThrowIfNull(process);

        LogPath = logPath;
        _file = new StreamWriter(logPath, append: true) { AutoFlush = true };
        process.OutputDataReceived += (_, line) => Add(line.Data);
        process.ErrorDataReceived += (_, line) => Add(line.Data);
    }

    public string LogPath { get; }

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lock)
            {
                return [.. _lines];
            }
        }
    }

    public string Text => string.Join(Environment.NewLine, Lines);

    public string Tail(int count = 40) => string.Join(Environment.NewLine, Lines.TakeLast(count));

    public int Count(string text) => Lines.Count(line => line.Contains(text, StringComparison.Ordinal));

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _file.Dispose();
        }
    }

    private void Add(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_lock)
        {
            _lines.Add(line);

            // A process that was not waited for can still write after the file was closed.
            if (!_disposed)
            {
                _file.WriteLine(line);
            }
        }
    }
}
