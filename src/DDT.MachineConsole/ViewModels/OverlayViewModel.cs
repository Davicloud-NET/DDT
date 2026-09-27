// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// What opens over the screen on a key: the log, the machine's details, the licences. Esc or the same key closes it.
public abstract class OverlayViewModel : ScreenViewModel
{
    protected OverlayViewModel(Localizer localizer)
        : base(localizer) => CloseCommand = new Command(() => Closing?.Invoke());

    public Command CloseCommand { get; }

    public abstract string CloseLabel { get; }

    // Set by the console, which knows what is open.
    public Action? Closing { get; set; }
}
