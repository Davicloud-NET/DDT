// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DDT.MachineConsole.ViewModels;

// Change notification for the compiled bindings of the views, without reflection.
public abstract class ObservableObject : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs s_everything = new(string.Empty);

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);

        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // Raises a change for every property, like after the language changed.
    protected void RaiseAll() => PropertyChanged?.Invoke(this, s_everything);
}
