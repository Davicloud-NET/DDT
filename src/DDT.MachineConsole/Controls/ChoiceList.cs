// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole.Controls;

// A question's list, chosen from with the arrow keys. Enter submits the question, since a ListBoxItem takes Enter for
// itself.
internal static class ChoiceList
{
    public static void Attach(ListBox list)
    {
        list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public static void Focus(ListBox list) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (list.SelectedIndex >= 0 && list.ContainerFromIndex(list.SelectedIndex) is { } selected)
                {
                    selected.Focus(NavigationMethod.Directional);
                }
                else
                {
                    list.Focus(NavigationMethod.Directional);
                }
            },
            DispatcherPriority.Loaded);

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || sender is not ListBox { DataContext: QuestionViewModel question })
        {
            return;
        }

        question.SubmitCommand.Execute(null);
        e.Handled = true;
    }
}
