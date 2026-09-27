// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole.Views;

// The field the agent asks for has the focus, so the person just types.
public sealed partial class SignInView : UserControl
{
    private SignInViewModel? _model;

    public SignInView()
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

        _model = DataContext as SignInViewModel;

        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
            FocusField();
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        FocusField();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SignInViewModel.Field) or nameof(SignInViewModel.IsEditable) or "")
        {
            FocusField();
        }
    }

    // After the layout, once the field is visible and enabled.
    private void FocusField() =>
        Dispatcher.UIThread.Post(
            () =>
            {
                TextBox? field = _model?.Field switch
                {
                    SignInField.UserName => UserNameBox,
                    SignInField.Password => PasswordBox,
                    SignInField.Code => CodeBox,
                    _ => null,
                };

                if (field is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true })
                {
                    field.Focus();
                    field.CaretIndex = field.Text?.Length ?? 0;
                }
            },
            DispatcherPriority.Loaded);
}
