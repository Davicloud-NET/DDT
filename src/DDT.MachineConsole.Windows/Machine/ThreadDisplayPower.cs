// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.MachineConsole.Machine;

// The display-required state of the calling thread, the UI thread, which lives as long as the console. Windows drops it
// when the thread ends.
public sealed partial class ThreadDisplayPower : IDisplayPower
{
    private const uint Continuous = 0x80000000;
    private const uint DisplayRequired = 0x00000002;

    public IDisposable KeepOn()
    {
        _ = SetThreadExecutionState(Continuous | DisplayRequired);

        return new Hold();
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint flags);

    private sealed class Hold : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (!_released)
            {
                _released = true;
                _ = SetThreadExecutionState(Continuous);
            }
        }
    }
}
