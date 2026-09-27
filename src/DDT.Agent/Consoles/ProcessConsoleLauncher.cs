// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Diagnostics;
using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// Starts the console at path as `<path> --pipe <name>`. It runs as long as it likes: when the agent ends, the console
// sees its pipe close and decides what to show.
public sealed class ProcessConsoleLauncher(string path) : IConsoleLauncher
{
    public IConsoleProcess Start(string pipeName)
    {
        ProcessStartInfo start = new(path) { UseShellExecute = false };
        start.ArgumentList.Add(ConsolePipe.PipeArgument);
        start.ArgumentList.Add(pipeName);

        return new StartedConsole(Process.Start(start) ?? throw new InvalidOperationException($"{path} did not start."));
    }

    private sealed class StartedConsole(Process process) : IConsoleProcess
    {
        public Task<int> Exited { get; } = WaitAsync(process);

        public void Stop()
        {
            try
            {
                process.Kill();
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // It has ended already.
            }
        }

        public void Dispose() => process.Dispose();

        private static async Task<int> WaitAsync(Process process)
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

            return process.ExitCode;
        }
    }
}
