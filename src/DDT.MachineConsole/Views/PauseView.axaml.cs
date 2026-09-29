// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Controls;

namespace DDT.MachineConsole.Views;

// Nothing on the screen handles Enter itself, so the key under the panel gets it wherever the focus is.
public sealed partial class PauseView : UserControl
{
    public PauseView()
    {
        InitializeComponent();
    }
}
