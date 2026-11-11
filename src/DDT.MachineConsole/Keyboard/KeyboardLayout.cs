// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using System.Globalization;

namespace DDT.MachineConsole.Keyboard;

// What each key of one layout types and what its dead keys make, from a .keymap file of make_layouts.py. A cell is a
// code point, the negated combining mark of a dead key, or 0 for nothing.
public sealed class KeyboardLayout
{
    private const int Cells = 8;

    private readonly FrozenDictionary<ushort, int[]> _keys;

    private KeyboardLayout(string name, bool altGr, FrozenDictionary<ushort, int[]> keys, DeadKeys deadKeys)
    {
        Name = name;
        AltGr = altGr;
        _keys = keys;
        DeadKeys = deadKeys;
    }

    // The file's name, like de or ch-fr.
    public string Name { get; }

    // True if right Alt is AltGr, false if it is a second Alt.
    public bool AltGr { get; }

    public IEnumerable<ushort> Codes => _keys.Keys;

    public DeadKeys DeadKeys { get; }

    public int Cell(ushort code, bool shift, bool altGr, bool caps) =>
        _keys.TryGetValue(code, out int[]? cells) ? cells[(caps ? 4 : 0) + (altGr ? 2 : 0) + (shift ? 1 : 0)] : 0;

    public static KeyboardLayout Parse(string name, string text)
    {
        bool? altGr = null;
        Dictionary<ushort, int[]> keys = [];
        Dictionary<(int, int), int> dead = [];

        foreach (string line in text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            string[] parts = line.Split(' ');

            if (parts is ["altgr", string flag])
            {
                altGr = flag == "1";
            }
            else if (parts is ["dead", string mark, string next, string made])
            {
                dead[(Hex(mark), Hex(next))] = Hex(made);
            }
            else if (parts.Length == Cells + 1)
            {
                keys.Add(ushort.Parse(parts[0], CultureInfo.InvariantCulture), [.. parts.Skip(1).Select(ParseCell)]);
            }
            else
            {
                throw new FormatException($"The keyboard layout {name} has a line that is no key: {line}");
            }
        }

        return altGr is bool level3
            ? new KeyboardLayout(name, level3, keys.ToFrozenDictionary(), new DeadKeys(dead.ToFrozenDictionary()))
            : throw new FormatException($"The keyboard layout {name} doesn't say what right Alt is.");
    }

    private static int ParseCell(string cell) => cell switch
    {
        "-" => 0,
        ['^', .. string mark] => -Hex(mark),
        _ => Hex(cell),
    };

    private static int Hex(string value) => int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
