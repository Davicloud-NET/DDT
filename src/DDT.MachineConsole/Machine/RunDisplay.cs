// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.MachineConsole.Machine;

// Keeps the screen on while the console shows a run, so a long step stays in sight without a key press.
public sealed class RunDisplay(IDisplayPower power) : IDisposable
{
    private IDisposable? _held;

    public bool IsOn => _held is not null;

    public void Follow(ConsoleState? state, bool ended)
    {
        bool shows = !ended && state is { Run: not null, Stage: ConsoleStage.Running or ConsoleStage.Restarting };

        if (shows && _held is null)
        {
            _held = power.KeepOn();
        }
        else if (!shows)
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        _held?.Dispose();
        _held = null;
    }
}
