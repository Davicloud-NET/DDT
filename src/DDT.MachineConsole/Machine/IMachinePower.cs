// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole.Machine;

// What the console may do to the machine once the agent has ended: restart it, which only ever happens in Windows PE.
public interface IMachinePower
{
    bool IsWindowsPE { get; }

    // False outside Windows PE, where the console never restarts anything.
    bool CanRestart { get; }

    void Restart();
}
