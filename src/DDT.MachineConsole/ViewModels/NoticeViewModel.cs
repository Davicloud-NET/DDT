// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// The short note above the keys, with the keys it names.
public sealed class NoticeViewModel(Localizer localizer) : ObservableObject
{
    private ConsoleNotice _current;

    // The band keeps the last note's text while it fades out.
    private ConsoleNotice _shown;

    public ConsoleNotice Current
    {
        get => _current;
        private set
        {
            if (value != ConsoleNotice.None)
            {
                _shown = value;
            }

            if (Set(ref _current, value))
            {
                Raise(nameof(IsShown));
                Raise(nameof(Text));
                Raise(nameof(Keys));
            }
        }
    }

    public bool IsShown => Current != ConsoleNotice.None;

    public string Text => _shown switch
    {
        ConsoleNotice.MediaKeys => localizer.T("The top row sends media keys. Hold Fn, or press Fn and Esc to lock the F keys."),
        ConsoleNotice.CloseRefused or ConsoleNotice.SessionCloseRefused => localizer.T("The console stays open while DDT works."),
        ConsoleNotice.SignOutWithF9 => localizer.T("Sign out"),
        _ => string.Empty,
    };

    public IReadOnlyList<string> Keys => _shown switch
    {
        ConsoleNotice.MediaKeys => ["Fn"],
        ConsoleNotice.SignOutWithF9 => ["F9"],
        _ => [],
    };

    public void Show(ConsoleNotice notice) => Current = notice;

    // Dismisses only that note. A different note stays.
    public void Dismiss(ConsoleNotice notice)
    {
        if (Current == notice)
        {
            Current = ConsoleNotice.None;
        }
    }

    public void Refresh() => RaiseAll();
}
