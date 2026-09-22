// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Records each tool call as the file name and its arguments joined by spaces, instead of running it. Answer decides
// what a call prints, or throws; without it every call prints nothing. AnswerExitCode decides the exit code of a
// call that asks for one, or throws; without it every such call ends with 0. Options holds each such call's options.
internal sealed class RecordingToolRunner : IToolRunner
{
    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];
    private readonly List<ToolRunOptions> _options = [];

    public Func<string, IReadOnlyList<string>, IReadOnlyList<string>>? Answer { get; set; }

    public Func<string, IReadOnlyList<string>, ToolRunOptions, int>? AnswerExitCode { get; set; }

    public List<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    public List<ToolRunOptions> Options
    {
        get
        {
            lock (_lock)
            {
                return [.. _options];
            }
        }
    }

    public static string CommandLine(string fileName, params string[] arguments) => string.Join(' ', [fileName, .. arguments]);

    public Task<IReadOnlyList<string>> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _calls.Add(CommandLine(fileName, [.. arguments]));
        }

        try
        {
            return Task.FromResult(Answer?.Invoke(fileName, arguments) ?? []);
        }
        catch (Exception exception)
        {
            return Task.FromException<IReadOnlyList<string>>(exception);
        }
    }

    public Task<int> RunForExitCodeAsync(string fileName, IReadOnlyList<string> arguments, ToolRunOptions options, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _calls.Add(CommandLine(fileName, [.. arguments]));
            _options.Add(options);
        }

        try
        {
            return Task.FromResult(AnswerExitCode?.Invoke(fileName, arguments, options) ?? 0);
        }
        catch (Exception exception)
        {
            return Task.FromException<int>(exception);
        }
    }
}
