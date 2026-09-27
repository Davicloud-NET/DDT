// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DDT.MachineConsole.Views;

// The field has the focus, so the person just types.
public sealed partial class ComputerNameView : UserControl
{
    public ComputerNameView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Dispatcher.UIThread.Post(() => NameBox.Focus(), DispatcherPriority.Loaded);
    }
}
