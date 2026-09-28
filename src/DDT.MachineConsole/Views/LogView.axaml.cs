// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole.Views;

// Follows new lines while the view is at the end. Scrolling up, with the wheel or Up, Page Up and Home, stops that. End
// or scrolling back to the last line starts it again.
public sealed partial class LogView : UserControl
{
    private LogViewModel? _model;
    private bool _scrolling;

    public LogView()
    {
        InitializeComponent();
        Scroller.ScrollChanged += OnScrollChanged;
        Scroller.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_model is not null)
        {
            _model.Lines.CollectionChanged -= OnLinesChanged;
        }

        _model = DataContext as LogViewModel;

        if (_model is not null)
        {
            _model.Lines.CollectionChanged += OnLinesChanged;
            FollowEnd();
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Scroller.Focus();
        FollowEnd();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_model is not null)
        {
            _model.Lines.CollectionChanged -= OnLinesChanged;
        }

        base.OnUnloaded(e);
    }

    private void OnFollow(object? sender, RoutedEventArgs e) => Follow();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.End)
        {
            Follow();
            e.Handled = true;
        }
    }

    private void Follow()
    {
        if (_model is not null)
        {
            _model.Follows = true;
        }

        FollowEnd();
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e) => FollowEnd();

    private void FollowEnd()
    {
        if (_model is not { Follows: true })
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                _scrolling = true;
                Scroller.ScrollToEnd();
                _scrolling = false;
            },
            DispatcherPriority.Background);
    }

    // Only a scroll of the view shows where the person wants to be. New lines just grow the log below it.
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_model is null)
        {
            return;
        }

        if (!_scrolling && e.OffsetDelta.Y != 0)
        {
            double fromEnd = Scroller.Extent.Height - Scroller.Viewport.Height - Scroller.Offset.Y;
            _model.Follows = fromEnd < 4;
        }
        else if (e.ExtentDelta.Y != 0 && _model.Follows)
        {
            FollowEnd();
        }
    }
}
