// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.MachineConsole.Native;

namespace DDT.MachineConsole.Screen;

// The virtual terminal the console draws on: the one it was started on, brought to the front. In graphics mode the
// kernel draws no text, cursor or messages over the picture. At the end it's text again, and the earlier one in front.
internal sealed class VirtualTerminal : IDisposable
{
    // VT_OPENQRY, VT_GETSTATE, VT_ACTIVATE, VT_WAITACTIVE and KDSETMODE of linux/vt.h and linux/kd.h.
    private const nuint OpenQuery = 0x5600;
    private const nuint GetState = 0x5603;
    private const nuint Activate = 0x5606;
    private const nuint WaitActive = 0x5607;
    private const nuint SetMode = 0x4B3A;
    private const nint TextMode = 0;
    private const nint GraphicsMode = 1;

    private readonly int _cameFrom;
    private int _descriptor;

    private VirtualTerminal(int descriptor, int number, int cameFrom)
    {
        _descriptor = descriptor;
        Number = number;
        _cameFrom = cameFrom;
    }

    public int Number { get; }

    // In front, if it wasn't. Null with the reason when the console runs on no virtual terminal, as over SSH.
    public static VirtualTerminal? Take(out string problem)
    {
        string path = Own() ?? "/dev/tty0";
        int descriptor = Libc.Open(path, Libc.ReadWrite | Libc.NoControllingTerminal | Libc.CloseOnExec);

        if (descriptor < 0)
        {
            problem = $"{path}: {Libc.Describe("open")}";

            return null;
        }

        int active = Active(descriptor);
        int number = path == "/dev/tty0" ? active : Parse(path);

        if (active <= 0 || number <= 0)
        {
            _ = Libc.Close(descriptor);
            problem = $"{path} is no virtual terminal.";

            return null;
        }

        if (active != number)
        {
            _ = Libc.Ioctl(descriptor, Activate, number);
            _ = Libc.Ioctl(descriptor, WaitActive, number);
        }

        VirtualTerminal terminal = new(descriptor, number, active);
        terminal.Graphics();
        problem = "";

        return terminal;
    }

    public bool IsActive => Active(_descriptor) == Number;

    public void Graphics() => _ = Libc.Ioctl(_descriptor, SetMode, GraphicsMode);

    // Before another terminal comes to the front: it would show this one's picture otherwise.
    public void Text() => _ = Libc.Ioctl(_descriptor, SetMode, TextMode);

    // The first terminal nobody uses, or 0.
    public unsafe int FirstFree()
    {
        int number = 0;

        return Libc.Ioctl(_descriptor, OpenQuery, &number) < 0 ? 0 : number;
    }

    public void Bring(int number) => _ = Libc.Ioctl(_descriptor, Activate, number);

    public void Dispose()
    {
        if (_descriptor < 0)
        {
            return;
        }

        Text();

        if (_cameFrom != Number && IsActive)
        {
            Bring(_cameFrom);
        }

        _ = Libc.Close(_descriptor);
        _descriptor = -1;
    }

    // The terminal on standard input, like /dev/tty1, as the agent or systemd gave it.
    private static string? Own()
    {
        try
        {
            string? target = new FileInfo("/proc/self/fd/0").LinkTarget;

            return target is not null && Parse(target) > 0 ? target : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static int Parse(string path) =>
        path.StartsWith("/dev/tty", StringComparison.Ordinal)
        && int.TryParse(path.AsSpan(8), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            ? number
            : 0;

    // struct vt_stat starts with the number of the terminal in front.
    private static unsafe int Active(int descriptor)
    {
        ushort* state = stackalloc ushort[3];

        return Libc.Ioctl(descriptor, GetState, state) < 0 ? 0 : state[0];
    }
}
