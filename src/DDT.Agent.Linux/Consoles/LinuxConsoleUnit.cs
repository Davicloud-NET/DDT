// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// The console as a transient unit: a user of its own with the screen's and the keyboards' groups, no network and no
// privileges, on a terminal above the six logind starts logins on. It comes back after a crash, not after F9 or when
// it has no screen. systemd forgets it once it ended.
internal sealed class LinuxConsoleUnit(TimeProvider time) : IConsoleUnit
{
    public const string Name = "ddt-console.service";

    // logind's NAutoVTs is 6, and X servers have used 7.
    private const int FirstTerminal = 8;
    private const int LastTerminal = 15;

    private static readonly TimeSpan s_look = TimeSpan.FromSeconds(5);

    // The command line of the unit's start.
    public static IReadOnlyList<string> Arguments(string program, string socket, int terminal) =>
    [
        $"--unit={Name}",
        "--collect",
        "--quiet",
        "--property=DynamicUser=yes",
        "--property=SupplementaryGroups=video input",
        "--property=StandardInput=tty",
        "--property=StandardOutput=journal",
        string.Create(CultureInfo.InvariantCulture, $"--property=TTYPath=/dev/tty{terminal}"),
        "--property=TTYReset=yes",
        "--property=TTYVHangup=yes",
        "--property=TTYVTDisallocate=yes",
        "--property=Restart=on-failure",
        "--property=RestartSec=2",
        // The console's exit codes for no pipe, a refused console and no screen.
        "--property=RestartPreventExitStatus=2 3 5",
        "--property=PrivateNetwork=yes",
        "--property=CapabilityBoundingSet=",
        "--property=NoNewPrivileges=yes",
        "--",
        program,
        ConsolePipe.PipeArgument,
        socket,
        ConsolePipe.SessionArgument,
    ];

    public int FreeTerminal()
    {
        int descriptor = ConsoleNativeMethods.Open("/dev/tty0", ConsoleNativeMethods.OpenTerminal);

        if (descriptor < 0)
        {
            return 0;
        }

        try
        {
            ushort open = OpenTerminals(descriptor);

            for (int terminal = FirstTerminal; terminal <= LastTerminal; terminal++)
            {
                if ((open & (1 << terminal)) == 0)
                {
                    return terminal;
                }
            }

            return 0;
        }
        finally
        {
            _ = ConsoleNativeMethods.Close(descriptor);
        }
    }

    public async Task<string?> StartAsync(string program, string socket, int terminal, CancellationToken cancellationToken)
    {
        (int exitCode, string errors) = await RunAsync("systemd-run", Arguments(program, socket, terminal), cancellationToken).ConfigureAwait(false);

        return exitCode == 0 ? null : errors;
    }

    public async Task StopAsync(CancellationToken cancellationToken) =>
        _ = await RunAsync("systemctl", ["stop", Name], cancellationToken).ConfigureAwait(false);

    // A console that restarts after a crash keeps its unit loaded. One that ended is forgotten.
    public async Task<bool> WaitUntilGoneAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                (int exitCode, string output) = await RunAsync("systemctl", ["show", "--property=LoadState", "--value", Name], cancellationToken).ConfigureAwait(false);

                if (exitCode != 0 || output.Trim() != "loaded")
                {
                    return true;
                }

                await Task.Delay(s_look, time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    // struct vt_stat: the terminal in front, then a bit for each terminal that is open.
    private static unsafe ushort OpenTerminals(int descriptor)
    {
        ushort* state = stackalloc ushort[3];

        return ConsoleNativeMethods.ioctl(descriptor, ConsoleNativeMethods.GetState, (nint)state) < 0 ? ushort.MaxValue : state[1];
    }

    // Quietly, unlike the run's tools: the wait asks every few seconds. Returns the standard output, or the errors.
    private static async Task<(int ExitCode, string Output)> RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{tool} did not start.");
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errors = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return process.ExitCode == 0
                ? (0, await output.ConfigureAwait(false))
                : (process.ExitCode, (await errors.ConfigureAwait(false)).Trim());
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return (-1, exception.Message);
        }
    }
}
