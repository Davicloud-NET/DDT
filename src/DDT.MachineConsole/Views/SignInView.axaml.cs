// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Controls;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole.Views;

// The field the agent asks for has the focus, so the person just types. A field asked for next enters, and so do the
// agent's words about each refused attempt, even where they are the same as before.
public sealed partial class SignInView : UserControl
{
    private SignInViewModel? _model;
    private (int Id, SignInField Field)? _asked;

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
        _asked = _model is null ? null : (_model.Id, _model.Field);

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

        if (_model is null || (_model.Id, _model.Field) == _asked)
        {
            return;
        }

        SignInField? before = _asked?.Field;
        _asked = (_model.Id, _model.Field);

        if (_model.Field != before)
        {
            Motion.Renew(_model.Field switch
            {
                SignInField.UserName => UserNameField,
                SignInField.Password => PasswordField,
                _ => CodeField,
            });
        }

        if (_model.HasError)
        {
            Motion.Renew(ErrorBox);
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
