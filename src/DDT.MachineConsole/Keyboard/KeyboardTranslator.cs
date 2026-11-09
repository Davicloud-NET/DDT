// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using Avalonia.Input;

namespace DDT.MachineConsole.Keyboard;

// Turns the key events of every keyboard into Avalonia's key strokes with their text: holds the modifiers over all
// devices, the two locks and a dead key. AltGr sets no modifier, and Ctrl, Alt or the Windows key type nothing.
public sealed class KeyboardTranslator(KeyboardLayout layout)
{
    // The navigation key of each keypad key without Num Lock.
    private static readonly FrozenDictionary<ushort, Key> s_keypadMoves = new Dictionary<ushort, Key>
    {
        [71] = Key.Home,
        [72] = Key.Up,
        [73] = Key.PageUp,
        [75] = Key.Left,
        [76] = Key.Clear,
        [77] = Key.Right,
        [79] = Key.End,
        [80] = Key.Down,
        [81] = Key.PageDown,
        [82] = Key.Insert,
        [83] = Key.Delete,
    }.ToFrozenDictionary();

    private readonly Dictionary<int, HashSet<ushort>> _held = [];

    // The key each held code went down as, so it goes up as the same: a keypad key moves or types by Num Lock.
    private readonly Dictionary<ushort, Key> _downAs = [];

    // The combining mark of a dead key that waits for the next key, or 0.
    private int _dead;

    public KeyboardLayout Layout
    {
        get => layout;
        set
        {
            layout = value;
            _dead = 0;
        }
    }

    public bool CapsLock { get; private set; }

    // The firmware's state comes first, from the keyboard's LED.
    public bool NumLock { get; set; }

    // One EV_KEY event of a device: 0 up, 1 down, 2 the kernel's repeat.
    public IReadOnlyList<KeyStroke> Feed(int device, EvdevEvent input)
    {
        if (input.Type != EvdevEvents.Key || EvdevKeys.Physical(input.Code) == PhysicalKey.None)
        {
            return [];
        }

        HashSet<ushort> held = HeldOn(device);

        return input.Value switch
        {
            0 => held.Remove(input.Code) ? [Up(input.Code)] : [],
            1 when held.Add(input.Code) => Remember(input.Code, Down(input.Code, repeat: false)),
            1 or 2 when held.Contains(input.Code) => Remember(input.Code, Down(input.Code, repeat: true)),
            _ => [],
        };
    }

    // After SYN_DROPPED, the keys the device holds now. Keys let go meanwhile go up; keys pressed meanwhile type
    // nothing, since half of their press is lost.
    public IReadOnlyList<KeyStroke> Resync(int device, IReadOnlyCollection<ushort> down)
    {
        HashSet<ushort> held = HeldOn(device);
        List<KeyStroke> strokes = [];

        foreach (ushort code in held.Where(code => !down.Contains(code)).ToList())
        {
            held.Remove(code);
            strokes.Add(Up(code));
        }

        held.UnionWith(down.Where(code => EvdevKeys.Physical(code) != PhysicalKey.None));

        return strokes;
    }

    // A keyboard that was unplugged lets go of its keys.
    public IReadOnlyList<KeyStroke> Forget(int device)
    {
        IReadOnlyList<KeyStroke> strokes = Resync(device, []);
        _held.Remove(device);

        return strokes;
    }

    private IReadOnlyList<KeyStroke> Down(ushort code, bool repeat)
    {
        PhysicalKey physical = EvdevKeys.Physical(code);

        if (IsModifier(code) || code is EvdevKeys.CapsLock or EvdevKeys.NumLock)
        {
            if (repeat)
            {
                return [];
            }

            CapsLock ^= code == EvdevKeys.CapsLock;
            NumLock ^= code == EvdevKeys.NumLock;

            return [new KeyStroke(true, physical.ToQwertyKey(), physical, Modifiers(), null)];
        }

        KeyModifiers modifiers = Modifiers();

        if (s_keypadMoves.TryGetValue(code, out Key move) && (!NumLock || modifiers.HasFlag(KeyModifiers.Shift)))
        {
            // Shift with Num Lock moves, as on Windows, and the field sees no Shift.
            return LetGoOfDeadKey(new KeyStroke(true, move, physical, modifiers & ~KeyModifiers.Shift, null));
        }

        bool types = (modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == KeyModifiers.None;
        bool altGr = IsHeld(EvdevKeys.RightAlt) && layout.AltGr;
        int cell = types ? layout.Cell(code, modifiers.HasFlag(KeyModifiers.Shift), altGr, CapsLock) : 0;
        KeyStroke stroke = new(true, KeyOf(code, physical), physical, modifiers, null);

        if (cell == 0)
        {
            return LetGoOfDeadKey(stroke);
        }

        if (repeat && cell < 0)
        {
            return [stroke];
        }

        return [stroke with { Text = Typed(cell) }];
    }

    // What a key with this cell types after whatever dead key waits.
    private string? Typed(int cell)
    {
        if (_dead == 0)
        {
            _dead = cell < 0 ? -cell : 0;

            return cell < 0 ? null : char.ConvertFromUtf32(cell);
        }

        string text = layout.DeadKeys.Then(_dead, cell);
        _dead = 0;

        return text;
    }

    // A key that types nothing gets a waiting dead key's accent first, as Windows types it.
    private IReadOnlyList<KeyStroke> LetGoOfDeadKey(KeyStroke stroke)
    {
        if (_dead == 0)
        {
            return [stroke];
        }

        string accent = layout.DeadKeys.Alone(_dead);
        _dead = 0;

        return [new KeyStroke(true, Key.None, PhysicalKey.None, stroke.Modifiers, accent), stroke];
    }

    private IReadOnlyList<KeyStroke> Remember(ushort code, IReadOnlyList<KeyStroke> strokes)
    {
        if (strokes is [.., KeyStroke last])
        {
            _downAs[code] = last.Key;
        }

        return strokes;
    }

    private KeyStroke Up(ushort code)
    {
        PhysicalKey physical = EvdevKeys.Physical(code);
        Key key = _downAs.GetValueOrDefault(code, KeyOf(code, physical));

        if (!IsHeld(code))
        {
            _downAs.Remove(code);
        }

        return new KeyStroke(false, key, physical, Modifiers(), null);
    }

    // The letter or digit the key has without modifiers, so Ctrl+A is A on any layout; else its US place.
    private Key KeyOf(ushort code, PhysicalKey physical)
    {
        int plain = s_keypadMoves.ContainsKey(code) ? 0 : layout.Cell(code, false, false, false);

        return plain switch
        {
            >= 'a' and <= 'z' => Key.A + (plain - 'a'),
            >= '0' and <= '9' => Key.D0 + (plain - '0'),
            _ => physical.ToQwertyKey(),
        };
    }

    private KeyModifiers Modifiers()
    {
        KeyModifiers modifiers = KeyModifiers.None;

        if (IsHeld(EvdevKeys.LeftShift) || IsHeld(EvdevKeys.RightShift))
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (IsHeld(EvdevKeys.LeftCtrl) || IsHeld(EvdevKeys.RightCtrl))
        {
            modifiers |= KeyModifiers.Control;
        }

        if (IsHeld(EvdevKeys.LeftAlt) || (IsHeld(EvdevKeys.RightAlt) && !layout.AltGr))
        {
            modifiers |= KeyModifiers.Alt;
        }

        if (IsHeld(EvdevKeys.LeftMeta) || IsHeld(EvdevKeys.RightMeta))
        {
            modifiers |= KeyModifiers.Meta;
        }

        return modifiers;
    }

    private static bool IsModifier(ushort code) => code is EvdevKeys.LeftShift or EvdevKeys.RightShift or EvdevKeys.LeftCtrl
        or EvdevKeys.RightCtrl or EvdevKeys.LeftAlt or EvdevKeys.RightAlt or EvdevKeys.LeftMeta or EvdevKeys.RightMeta;

    private bool IsHeld(ushort code) => _held.Values.Any(held => held.Contains(code));

    private HashSet<ushort> HeldOn(int device)
    {
        if (!_held.TryGetValue(device, out HashSet<ushort>? held))
        {
            held = [];
            _held[device] = held;
        }

        return held;
    }
}
