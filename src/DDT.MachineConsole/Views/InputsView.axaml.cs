// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DDT.MachineConsole.Controls;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole.Views;

// The first field to answer has the focus, so the person just types: the first the agent refused, or else the first.
// Enter sends the answers from any field, since a key of a choice would take Enter for itself; only the key that goes
// back keeps its own Enter. The agent's words about refused answers fade in with each refusal.
public sealed partial class InputsView : UserControl
{
    private InputsViewModel? _model;
    private int? _asked;

    public InputsView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = DataContext as InputsViewModel;
        _asked = _model?.Id;

        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        FocusField();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || _model is null || e.Source is Button and not ToggleButton)
        {
            return;
        }

        _model.SubmitCommand.Execute(null);
        e.Handled = true;
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
            Motion.Renew(ErrorBox);
        }

        foreach (TextBlock error in Fields.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Name == "FieldError" && text.IsVisible))
        {
            Motion.Renew(error);
        }

        FocusField();
    }

    // After the layout, once the fields are there and enabled. A choice takes the focus without choosing anything.
    private void FocusField() =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_model is null || _model.Fields.Count == 0)
                {
                    return;
                }

                int index = _model.Fields.ToList().FindIndex(field => field.HasError);

                if (Fields.ContainerFromIndex(Math.Max(0, index)) is not { } container)
                {
                    return;
                }

                InputElement? target = container.GetVisualDescendants()
                    .OfType<InputElement>()
                    .FirstOrDefault(element => element is TextBox or ListBox or ToggleButton && element.IsEffectivelyEnabled);

                switch (target)
                {
                    case TextBox box:
                        box.Focus(NavigationMethod.Tab);
                        box.CaretIndex = box.Text?.Length ?? 0;
                        break;
                    case ListBox list:
                        (list.ContainerFromIndex(Math.Max(0, list.SelectedIndex)) ?? list).Focus(NavigationMethod.Tab);
                        break;
                    default:
                        target?.Focus(NavigationMethod.Tab);
                        break;
                }
            },
            DispatcherPriority.Loaded);
}
