// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DDT.MachineConsole.Controls;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole.Views;

// The prefilled name is selected, so typing replaces it. The error animates in again with each refusal.
public sealed partial class ComputerNameView : UserControl
{
    private ComputerNameViewModel? _model;
    private int? _asked;

    public ComputerNameView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = DataContext as ComputerNameViewModel;
        _asked = _model?.Id;

        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Dispatcher.UIThread.Post(
            () =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            },
            DispatcherPriority.Loaded);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_model is null || _model.Id == _asked)
        {
            return;
        }

        _asked = _model.Id;

        if (_model.HasError)
        {
            Motion.Renew(Error);
        }
    }
}
