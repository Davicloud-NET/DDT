// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole.Machine;

// Keeps the screen on until the hold is disposed. Called and disposed on the UI thread.
public interface IDisplayPower
{
    IDisposable KeepOn();
}
