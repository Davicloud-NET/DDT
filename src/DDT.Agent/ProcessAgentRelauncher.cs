// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;

namespace DDT.Agent;

// beforeStart closes what this agent holds open while the new one runs, such as its connections to the server.
public sealed class ProcessAgentRelauncher(Action? beforeStart = null) : IAgentRelauncher
{
    public async Task<int> RunAsync(string path, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        beforeStart?.Invoke();

        // Without shell execution and redirection the new agent shares this console, so it can ask for a
        // sign in. startnet.cmd waits on this process, which in turn waits on the new one.
        ProcessStartInfo start = new(path) { UseShellExecute = false };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{path} did not start.");

        // Not cancellable: Ctrl+C reaches the new agent as well, which stops on its own.
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

        return process.ExitCode;
    }
}
