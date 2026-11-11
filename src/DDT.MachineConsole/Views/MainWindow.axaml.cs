// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Controls;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole.Views;

// In WinPE and as the shell of DDT's session the window fills the screen without a frame. On a development computer
// it's an ordinary window.
public sealed partial class MainWindow : Window
{
    private readonly MainViewModel? _model;

    // For the XAML designer and loader only.
    public MainWindow()
    {
        InitializeComponent();
        Content = new MainView();
    }

    public MainWindow(MainViewModel model, bool fullScreen)
    {
        ArgumentNullException.ThrowIfNull(model);

        InitializeComponent();
        _model = model;
        DataContext = model;

        // Full screen but never topmost, so the command prompt Shift+F10 opens comes in front of it.
        if (fullScreen)
        {
            WindowDecorations = WindowDecorations.None;
            WindowState = WindowState.FullScreen;
        }

        Content = new MainView(model);
    }

    // The console takes the foreground only once, when it opens. It never takes it back later, so a command prompt in
    // front of it keeps the keyboard.
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Activate();
    }

    // Alt+F4 or the close button while the agent works is refused with a note, so a passer-by can't close the console.
    // The console's own close, a shutdown and a restart go through unchanged.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.CloseReason == WindowCloseReason.WindowClosing && !e.IsProgrammatic && _model?.RefuseClose() == true)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
