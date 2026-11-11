// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Records what each start as an account was given, and hands back a tool that printed nothing and ended with 0.
internal sealed class RecordingAccountProcessStarter : IAccountProcessStarter
{
    public List<(string FileName, ToolRunOptions Options)> Starts { get; } = [];

    public IToolProcess Start(IAccountSession account, string fileName, IReadOnlyList<string> arguments, ToolRunOptions options)
    {
        Starts.Add((fileName, options));

        return new Ended();
    }

    private sealed class Ended : IToolProcess
    {
        public Stream StandardOutput => Stream.Null;

        public Stream StandardError => Stream.Null;

        public int ExitCode => 0;

        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task KillAsync() => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
