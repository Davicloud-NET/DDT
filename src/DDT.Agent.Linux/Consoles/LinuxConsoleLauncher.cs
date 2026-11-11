// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Diagnostics;
using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// The console on the agent's terminal. Stopped, it gets SIGTERM and gives the terminal back itself; one that is still
// there a second later is killed. However it ended, the terminal is put back into text mode, or the agent's lines
// would stay hidden behind the last picture.
public sealed class LinuxConsoleLauncher(string path) : IConsoleLauncher
{
    private static readonly TimeSpan s_grace = TimeSpan.FromSeconds(1);

    public IConsoleProcess Start(string pipeName)
    {
        ProcessStartInfo start = new(path) { UseShellExecute = false };
        start.ArgumentList.Add(ConsolePipe.PipeArgument);
        start.ArgumentList.Add(pipeName);

        return new StartedConsole(Process.Start(start) ?? throw new InvalidOperationException($"{path} did not start."));
    }

    // The terminal on the agent's standard input, which the console drew on.
    public static void TextMode() =>
        _ = ConsoleNativeMethods.ioctl(ConsoleNativeMethods.StandardInput, ConsoleNativeMethods.SetMode, ConsoleNativeMethods.TextMode);

    private sealed class StartedConsole(Process process) : IConsoleProcess
    {
        public Task<int> Exited { get; } = WaitAsync(process);

        public void Stop()
        {
            if (!Exited.IsCompleted && ConsoleNativeMethods.kill(process.Id, ConsoleNativeMethods.Terminate) == 0)
            {
                _ = KillLateAsync();
            }
        }

        public void Dispose() => process.Dispose();

        private static async Task<int> WaitAsync(Process process)
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            TextMode();

            return process.ExitCode;
        }

        private async Task KillLateAsync()
        {
            if (await Task.WhenAny(Exited, Task.Delay(s_grace)).ConfigureAwait(false) == Exited)
            {
                return;
            }

            try
            {
                process.Kill();
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // It has ended meanwhile.
            }
        }
    }
}
