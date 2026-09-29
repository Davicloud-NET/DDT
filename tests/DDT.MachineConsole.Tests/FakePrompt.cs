// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Machine;

namespace DDT.MachineConsole.Tests;

internal sealed class FakePrompt : ICommandPrompt
{
    public int Opened { get; private set; }

    public void Open() => Opened++;
}
