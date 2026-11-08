// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Controls;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole.Views;

// The console's whole screen, hosted by a window or by a screen without windows. The function keys and Esc reach the
// console before any field or list sees them.
public sealed partial class MainView : UserControl
{
    private static readonly ScreenTransition s_overlaySwitch = new();
    private readonly MainViewModel? _model;
    private TopLevel? _top;
    private FrameMeter? _frames;
    private (ConsoleStage? Stage, Guid? Step) _step;

    // For the XAML designer and loader only.
    public MainView()
    {
        InitializeComponent();
    }

    public MainView(MainViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        InitializeComponent();
        _model = model;
        DataContext = model;
        model.PropertyChanged += OnModelChanged;
        ApplyTheme(model.IsDark);
        ShowOverlayContent();
        _step = StepOf(model);
    }

    // The keys go to the top level first, because with nothing focused a key never reaches this view.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _top = TopLevel.GetTopLevel(this);

        if (_top is null || _model is null)
        {
            return;
        }

        _top.AddHandler(KeyDownEvent, OnKeyDownFirst, RoutingStrategies.Tunnel);
        _frames = FrameMeter.For(_top);

        if (_frames is not null)
        {
            _model.End.PropertyChanged += OnEndChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _top?.RemoveHandler(KeyDownEvent, OnKeyDownFirst);
        _top = null;

        if (_model is not null)
        {
            _model.End.PropertyChanged -= OnEndChanged;
        }
    }

    private void OnKeyDownFirst(object? sender, KeyEventArgs e)
    {
        _frames?.Measure(FrameMeter.KeyName(e.Key), Motion.Press);

        if (_model is not null && _model.Press(e.Key, e.KeyModifiers))
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

        if (_frames is not null)
        {
            MeasureFrames(_frames, _model, e.PropertyName);
        }

        if (e.PropertyName is nameof(MainViewModel.IsDark) or "")
        {
            ApplyTheme(_model.IsDark);
        }

        if (e.PropertyName is nameof(MainViewModel.OverlayContent) or "")
        {
            ShowOverlayContent();
        }

        if (e.PropertyName is nameof(MainViewModel.HasOverlay) && !_model.HasOverlay)
        {
            Dispatcher.UIThread.Post(FocusScreen, DispatcherPriority.Loaded);
        }
    }

    // Tells the frame meter what moves when that property changes, and for how long.
    private void MeasureFrames(FrameMeter frames, MainViewModel model, string? change)
    {
        switch (change)
        {
            case nameof(MainViewModel.Screen):
                frames.Measure("screen", Motion.Fast + Motion.Normal);
                _step = StepOf(model);
                break;
            case nameof(MainViewModel.HasOverlay):
                frames.Measure(model.HasOverlay ? "overlay in" : "overlay out", model.HasOverlay ? Motion.Normal : Motion.Fast);
                break;
            case nameof(MainViewModel.State) when StepOf(model) != _step:
                _step = StepOf(model);
                frames.Measure("step", Motion.Slow);
                break;
        }
    }

    private void OnEndChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_frames is null || _model is null)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(EndViewModel.ShowsEndBand):
                _frames.Measure("ended band", Motion.Normal);
                break;
            case nameof(EndViewModel.ConfirmingRestart):
                bool shown = _model.End.ConfirmingRestart;
                _frames.Measure(shown ? "confirm in" : "confirm out", shown ? Motion.Normal : Motion.Fast);
                break;
        }
    }

    private static (ConsoleStage? Stage, Guid? Step) StepOf(MainViewModel model) => (model.State?.Stage, model.State?.Run?.CurrentStepId);

    // What the overlay shows. When it closes, it keeps its content while it leaves. When it opens, the new content
    // shows right away as the overlay enters. Only a switch from one overlay to another plays a transition.
    private void ShowOverlayContent()
    {
        if (_model?.OverlayContent is not { } content || ReferenceEquals(OverlayPages.Content, content))
        {
            return;
        }

        bool switching = OverlayPages.Content is not null && Overlay.IsVisible && Overlay.IsHitTestVisible;
        OverlayPages.PageTransition = switching && Motion.IsEnabled ? s_overlaySwitch : null;
        OverlayPages.Content = content;
    }

    // After the log or the details close, the screen's field or list gets the focus back, so typing continues there.
    // That's the screen now shown, not one that's still leaving.
    private void FocusScreen()
    {
        InputElement? target = Screen.GetVisualDescendants()
            .OfType<InputElement>()
            .FirstOrDefault(element => element is TextBox or ListBoxItem or ListBox
                && element.IsEffectivelyVisible
                && element.IsEffectivelyEnabled
                && (element is not ListBoxItem item || item.IsSelected)
                && IsOnScreenNow(element));

        target?.Focus(NavigationMethod.Directional);
    }

    private bool IsOnScreenNow(Visual element) =>
        element.GetVisualAncestors().OfType<ContentPresenter>().LastOrDefault(presenter => presenter.TemplatedParent == Screen) is { } page
        && ReferenceEquals(page.Content, _model?.Screen);

    // A new theme changes every colour at once. The switching class stops controls that fade their colour on hover or
    // change from fading into the new theme.
    private void ApplyTheme(bool dark)
    {
        ThemeVariant theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        if (Application.Current is not { } application || application.RequestedThemeVariant == theme)
        {
            return;
        }

        Classes.Add("switching");
        application.RequestedThemeVariant = theme;
        Classes.Remove("switching");
    }
}
