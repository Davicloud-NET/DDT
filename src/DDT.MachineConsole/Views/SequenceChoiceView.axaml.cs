// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Controls;
using Avalonia.Interactivity;
using DDT.MachineConsole.Controls;

namespace DDT.MachineConsole.Views;

// The list has the focus, so the arrow keys choose at once and Enter sends the choice.
public sealed partial class SequenceChoiceView : UserControl
{
    public SequenceChoiceView()
    {
        InitializeComponent();
        ChoiceList.Attach(List);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ChoiceList.Focus(List);
    }
}
