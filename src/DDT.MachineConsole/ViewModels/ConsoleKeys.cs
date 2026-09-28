// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Input;

namespace DDT.MachineConsole.ViewModels;

// The keys that work whatever the screen shows, as a table from key to command. A key whose command cannot run now
// goes on to the screen.
public sealed class ConsoleKeys(NoticeViewModel notice, EndViewModel end, Command openPrompt, IReadOnlyDictionary<Key, Command> commands)
{
    // True when the console used the key. Only Shift+F10 takes a modifier; every other key works alone.
    public bool Press(Key key, KeyModifiers modifiers)
    {
        // The note about closing has been read once the person presses on.
        notice.Dismiss(ConsoleNotice.CloseRefused);

        if (key == Key.F10 && modifiers == KeyModifiers.Shift)
        {
            openPrompt.Execute(null);

            return true;
        }

        if (modifiers != KeyModifiers.None)
        {
            return false;
        }

        if (IsMediaKey(key))
        {
            notice.Show(ConsoleNotice.MediaKeys);

            return true;
        }

        // A function key came through, so the person has found Fn.
        if (key is >= Key.F1 and <= Key.F12 || key == Key.Escape)
        {
            notice.Dismiss(ConsoleNotice.MediaKeys);
        }

        if (end.ConfirmingRestart && key is Key.Enter or Key.Escape)
        {
            (key == Key.Enter ? end.ConfirmRestartCommand : end.CancelRestartCommand).Execute(null);

            return true;
        }

        if (!commands.TryGetValue(key, out Command? command) || !command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);

        return true;
    }

    // What a laptop's top row sends without Fn and Windows PE still turns into keys. Brightness and the like go to the
    // firmware and never arrive.
    private static bool IsMediaKey(Key key) => key is Key.VolumeMute or Key.VolumeDown or Key.VolumeUp
        or Key.MediaPlayPause or Key.MediaNextTrack or Key.MediaPreviousTrack or Key.MediaStop
        or Key.BrowserBack or Key.BrowserForward or Key.BrowserRefresh or Key.BrowserSearch or Key.BrowserHome;
}
