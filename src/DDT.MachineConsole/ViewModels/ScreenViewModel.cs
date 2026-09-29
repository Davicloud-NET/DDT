// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// What fills the space between the header and the footer: a stage of the agent, or the question it asks.
public abstract class ScreenViewModel(Localizer localizer) : ObservableObject
{
    protected Localizer L => localizer;

    // Refreshes everything the screen says in the newly chosen language.
    public virtual void Refresh() => RaiseAll();

    protected string T(string message) => localizer.T(message);

    protected string F(string message, params ReadOnlySpan<(string Name, string Value)> values) => localizer.F(message, values);
}
