// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Input;

namespace DDT.MachineConsole.Screen;

// Shows the picture and takes the keys only while the console's terminal is in front. Away, the terminal is in text
// mode and the keyboards are the kernel's; back, both return and the screen gets the whole picture.
internal sealed class ScreenKeeper(EvdevDevices devices, KeptFramebuffer output)
{
    // How long a terminal brought to the front on purpose has to come, before the console takes the screen back.
    private const long LeavingMilliseconds = 5000;

    private readonly Lock _gate = new();
    private VirtualTerminal? _terminal;
    private bool _present = true;
    private long _leftUntil;

    public bool Present
    {
        get
        {
            lock (_gate)
            {
                return _present;
            }
        }
    }

    // Null when the console runs on no virtual terminal.
    public VirtualTerminal? Terminal
    {
        get
        {
            lock (_gate)
            {
                return _terminal;
            }
        }
    }

    // Once the agent has accepted the console, before anything is drawn.
    public void Take(VirtualTerminal? terminal)
    {
        lock (_gate)
        {
            _terminal = terminal;
        }
    }

    // Looks at which terminal is in front, on the input thread. True when the console has just come back.
    public bool Look()
    {
        lock (_gate)
        {
            if (_terminal is not { } terminal)
            {
                return false;
            }

            bool active = terminal.IsActive;

            if (_present && !active)
            {
                Away();
            }
            else if (!_present && active && Environment.TickCount64 >= _leftUntil)
            {
                Back();

                return true;
            }

            return false;
        }
    }

    // Before the console brings another terminal to the front itself.
    public void Leave()
    {
        lock (_gate)
        {
            if (_present)
            {
                Away();
                _leftUntil = Environment.TickCount64 + LeavingMilliseconds;
            }
        }
    }

    // When what it left for has ended: its own terminal to the front, taken back at the next look.
    public void Return()
    {
        lock (_gate)
        {
            _leftUntil = 0;

            if (!_present && _terminal is { } terminal)
            {
                terminal.Bring(terminal.Number);
            }
        }
    }

    private void Away()
    {
        _present = false;
        output.Show(false);
        _terminal?.Text();
        devices.LetGo();
    }

    private void Back()
    {
        _present = true;
        _terminal?.Graphics();
        devices.Hold();
        output.Show(true);
    }
}
