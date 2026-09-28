// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole.ViewModels;

// A key of a choice. IsChosen is for the keys that turn on and off; a single choice is its list's selection.
public sealed class ChoiceItem(string value, Func<string> label, Action? changed = null) : ObservableObject
{
    private bool _isChosen;

    public string Value => value;

    public string Label => label();

    public bool IsChosen
    {
        get => _isChosen;
        set
        {
            if (Set(ref _isChosen, value))
            {
                changed?.Invoke();
            }
        }
    }

    public void Refresh() => Raise(nameof(Label));
}
