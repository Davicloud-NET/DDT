// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using System.Text;

namespace DDT.MachineConsole.Keyboard;

// What a layout's dead keys type with the key after them: the letters Windows makes, never X11's compose sequences.
public sealed class DeadKeys
{
    private const int Space = 0x20;

    private readonly FrozenDictionary<(int Mark, int Next), int> _made;

    internal DeadKeys(FrozenDictionary<(int Mark, int Next), int> made) => _made = made;

    // The accent a dead key types by itself, which is what it makes with a space.
    public string Alone(int mark) => Text(_made.GetValueOrDefault((mark, Space), mark));

    // A dead key, then a key that types next or is a dead key too (negated, as in a layout's cells).
    public string Then(int mark, int next)
    {
        if (next < 0)
        {
            return -next == mark ? Alone(mark) : Alone(mark) + Alone(-next);
        }

        return _made.TryGetValue((mark, next), out int made) ? Text(made) : Alone(mark) + Text(next);
    }

    private static string Text(int codePoint) => new Rune(codePoint).ToString();
}
