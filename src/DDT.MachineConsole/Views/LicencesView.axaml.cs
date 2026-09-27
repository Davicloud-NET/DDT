// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DDT.MachineConsole.Views;

// The list of texts has the focus, so Up and Down choose one.
public sealed partial class LicencesView : UserControl
{
    public LicencesView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Dispatcher.UIThread.Post(() => Documents.Focus(), DispatcherPriority.Loaded);
    }
}
