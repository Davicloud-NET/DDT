// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Keyboard;

namespace DDT.MachineConsole.Tests.Keyboard;

// The committed tables and a keyboard to press keys on, as evdev would send them.
internal sealed class Keys
{
    public const ushort Shift = EvdevKeys.LeftShift;
    public const ushort AltGr = EvdevKeys.RightAlt;
    public const ushort Ctrl = EvdevKeys.LeftCtrl;
    public const ushort Enter = 28;
    public const ushort Space = 57;
    public const ushort F10 = 68;

    public Keys(string layout = "de")
    {
        Translator = new KeyboardTranslator(Layout(layout));
    }

    public KeyboardTranslator Translator { get; }

    public List<KeyStroke> Strokes { get; } = [];

    public string Typed => string.Concat(Strokes.Select(stroke => stroke.Text));

    public static KeyboardLayout Layout(string name) => KeyboardLayout.Parse(name, Read($"{name}.keymap"));

    public static string Read(string name) => File.ReadAllText(Path.Combine(Repository.Console, "Keyboard", "Layouts", name));

    public Keys Down(ushort code, int device = 1) => Send(device, code, 1);

    public Keys Up(ushort code, int device = 1) => Send(device, code, 0);

    public Keys Repeat(ushort code, int device = 1) => Send(device, code, 2);

    public Keys Tap(ushort code, params ushort[] holding)
    {
        foreach (ushort modifier in holding)
        {
            Down(modifier);
        }

        Down(code).Up(code);

        foreach (ushort modifier in holding.Reverse())
        {
            Up(modifier);
        }

        return this;
    }

    // Types text with the keys the layout has for it, as a person would: the first key and level that give it.
    public Keys Type(string text)
    {
        KeyboardLayout layout = Translator.Layout;

        foreach (char wanted in text)
        {
            (ushort code, bool shift, bool altGr) = (
                from key in layout.Codes.Where(key => key is < 55 or 57 or 86).Order()
                from level in new[] { (false, false), (true, false), (false, true), (true, true) }
                where layout.Cell(key, level.Item1, level.Item2, false) == wanted
                select (key, level.Item1, level.Item2)).First();
            Tap(code, [.. shift ? new[] { Shift } : [], .. altGr ? new[] { AltGr } : []]);
        }

        return this;
    }

    private Keys Send(int device, ushort code, int value)
    {
        Strokes.AddRange(Translator.Feed(device, new EvdevEvent(EvdevEvents.Key, code, value)));

        return this;
    }
}
