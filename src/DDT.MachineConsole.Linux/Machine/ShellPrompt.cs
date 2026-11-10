// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using DDT.MachineConsole.Screen;

namespace DDT.MachineConsole.Machine;

// Shift+F10 in the boot environment: a root shell on a terminal of its own, as Windows Setup opens a command prompt.
// When it ends, openvt brings the console's terminal back. A second press goes to the shell that is open.
internal sealed class ShellPrompt(ScreenKeeper keeper) : ICommandPrompt
{
    private Process? _shell;
    private int _number;

    public void Open()
    {
        if (keeper.Terminal is not { } terminal)
        {
            return;
        }

        if (_shell is { HasExited: false })
        {
            keeper.Leave();
            terminal.Bring(_number);

            return;
        }

        int number = terminal.FirstFree();

        if (number <= 0)
        {
            return;
        }

        keeper.Leave();
        _shell?.Dispose();
        _shell = Start(number);
        _number = number;
    }

    private static Process? Start(int number)
    {
        ProcessStartInfo start = new("openvt") { UseShellExecute = false, WorkingDirectory = "/root" };

        foreach (string argument in new[] { "-c", number.ToString(CultureInfo.InvariantCulture), "-s", "-w", "/bin/sh", "-l" })
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(start);
        }
        catch (Win32Exception)
        {
            // Without openvt the console takes its screen back after a few seconds.
            return null;
        }
    }
}
