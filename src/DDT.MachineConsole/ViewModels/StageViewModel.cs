// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// A screen that shows a stage of the agent and follows its state.
public abstract class StageViewModel(Localizer localizer) : ScreenViewModel(localizer)
{
    public abstract void Update(ConsoleState state);
}
