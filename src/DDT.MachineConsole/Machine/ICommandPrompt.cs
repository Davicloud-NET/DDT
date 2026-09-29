// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole.Machine;

// The command prompt a technician opens with Shift+F10 while the console runs, like in Windows Setup.
public interface ICommandPrompt
{
    void Open();
}
