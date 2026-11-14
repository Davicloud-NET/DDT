// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Machine;

namespace DDT.MachineConsole.Tests;

// Counts the holds still open. Never touches this PC's display.
internal sealed class FakeDisplayPower : IDisplayPower
{
    public int Holds { get; private set; }

    public int Open { get; private set; }

    public IDisposable KeepOn()
    {
        Holds++;
        Open++;

        return new Release(this);
    }

    private sealed class Release(FakeDisplayPower owner) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (!_released)
            {
                _released = true;
                owner.Open--;
            }
        }
    }
}
