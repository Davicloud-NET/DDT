// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole.Views;

// In Windows PE the window fills the screen without a frame; on a development computer it is an ordinary window.
// The function keys and Esc reach the console before any field or list sees them.
public sealed partial class MainWindow : Window
{
    private readonly MainViewModel? _model;

    // For the XAML designer and loader only.
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainViewModel model, bool fullScreen)
    {
        ArgumentNullException.ThrowIfNull(model);

        InitializeComponent();
        _model = model;
        DataContext = model;

        if (fullScreen)
        {
            WindowDecorations = WindowDecorations.None;
            WindowState = WindowState.FullScreen;
        }

        AddHandler(KeyDownEvent, OnKeyDownFirst, RoutingStrategies.Tunnel);
        model.PropertyChanged += OnModelChanged;
        ApplyTheme(model.IsDark);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Activate();
    }

    private void OnKeyDownFirst(object? sender, KeyEventArgs e)
    {
        if (_model is not null && e.KeyModifiers == KeyModifiers.None && _model.Press(e.Key))
        {
            e.Handled = true;
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_model is null)
        {
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.IsDark) or "")
        {
            ApplyTheme(_model.IsDark);
        }

        if (e.PropertyName is nameof(MainViewModel.HasOverlay) && !_model.HasOverlay)
        {
            Dispatcher.UIThread.Post(FocusScreen, DispatcherPriority.Loaded);
        }
    }

    // Back from the log or the details, the screen's field or list has the focus again, so typing goes on there.
    private void FocusScreen()
    {
        InputElement? target = Screen.GetVisualDescendants()
            .OfType<InputElement>()
            .FirstOrDefault(element => element is TextBox or ListBoxItem or ListBox
                && element.IsEffectivelyVisible
                && element.IsEffectivelyEnabled
                && (element is not ListBoxItem item || item.IsSelected));

        target?.Focus(NavigationMethod.Directional);
    }

    private static void ApplyTheme(bool dark)
    {
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
