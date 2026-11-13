// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DDT.MachineConsole.Controls;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole.Views;

// Typing filters. Down goes from the search into the rows and Up from the first row back; Space ticks a row, and
// typing in the rows goes on in the search. Enter belongs to the inputs page, which sends the answers.
public sealed partial class ChoiceSearchView : UserControl
{
    public ChoiceSearchView()
    {
        InitializeComponent();
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        ChoicesList.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        ChoicesList.AddHandler(TextInputEvent, OnListTextInput, RoutingStrategies.Tunnel);
    }

    private MultiChoiceFieldViewModel? Model => DataContext as MultiChoiceFieldViewModel;

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down || e.KeyModifiers != KeyModifiers.None || Model is not { Shown.Count: > 0 } model)
        {
            return;
        }

        if (model.Highlighted is null)
        {
            model.Move(1);
        }

        ChoiceList.Focus(ChoicesList);
        e.Handled = true;
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || Model is not { } model)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                model.TickHighlighted();
                e.Handled = true;
                break;
            case Key.Up when ChoicesList.SelectedIndex <= 0:
                FocusSearch();
                e.Handled = true;
                break;
            case Key.Back:
                model.Search = model.Search.Length > 0 ? model.Search[..^1] : string.Empty;
                FocusSearch();
                e.Handled = true;
                break;
        }
    }

    // A space ticks, so only other text goes on in the search.
    private void OnListTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Text) || Model is not { } model)
        {
            return;
        }

        model.Search += e.Text;
        FocusSearch();
        e.Handled = true;
    }

    private void FocusSearch()
    {
        SearchBox.Focus(NavigationMethod.Tab);
        SearchBox.CaretIndex = SearchBox.Text?.Length ?? 0;
    }
}
