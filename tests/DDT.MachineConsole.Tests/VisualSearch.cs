// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Controls;

namespace DDT.MachineConsole.Tests;

internal static class VisualSearch
{
    // The control of that name on the screen, wherever it is in the views.
    public static T Find<T>(this Window window, string name)
        where T : Control =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<T>().FirstOrDefault(control => control.Name == name)
            ?? throw new InvalidOperationException($"No {name} on the screen.");
}
