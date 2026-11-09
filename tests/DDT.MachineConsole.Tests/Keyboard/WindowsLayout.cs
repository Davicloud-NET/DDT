// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DDT.MachineConsole.Tests.Keyboard;

// What a key types in a layout Windows ships, read from the layout DLL's own tables (kbd.h), so nothing is loaded into
// the session. A cell is a character, the accent of a dead key (Dead), or 0.
[SupportedOSPlatform("windows")]
internal sealed class WindowsLayout
{
    private const int Shift = 1;
    private const int AltGr = 6;
    private const ushort None = 0xF000;
    private const ushort DeadMark = 0xF001;
    private const byte CapsLock = 0x01;
    private const byte CapsLockSecond = 0x02;
    private const byte CapsLockAltGr = 0x04;
    private const byte DeadRow = 0xFF;

    private readonly Dictionary<byte, (byte Attributes, ushort[] Chars, ushort[]? Caps, ushort[]? Dead)> _keys = [];
    private readonly byte[] _modifierColumns;
    private readonly ushort[] _scanCodes;

    private WindowsLayout(nint tables)
    {
        nint modifiers = Marshal.ReadIntPtr(tables, 0);
        int maxBits = Marshal.ReadInt16(modifiers, 8);
        _modifierColumns = [.. Enumerable.Range(0, maxBits + 1).Select(bits => Marshal.ReadByte(modifiers, 10 + bits))];
        nint scanCodes = Marshal.ReadIntPtr(tables, 48);
        _scanCodes = [.. Enumerable.Range(0, Marshal.ReadByte(tables, 56)).Select(code => (ushort)Marshal.ReadInt16(scanCodes, code * 2))];
        HasAltGr = (Marshal.ReadInt32(tables, 80) & 1) != 0;
        ReadKeys(Marshal.ReadIntPtr(tables, 8));
        ReadDeadKeys(Marshal.ReadIntPtr(tables, 16));
    }

    public bool HasAltGr { get; }

    // What a dead key's accent and the next character make; a pair that isn't here types the accent and the character.
    public Dictionary<(char Accent, char Next), char> DeadKeys { get; } = [];

    public static WindowsLayout Load(string id)
    {
        using RegistryKey key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\{id}")
            ?? throw new InvalidOperationException($"Windows has no keyboard layout {id}.");
        string file = Path.Combine(Environment.SystemDirectory, (string)key.GetValue("Layout File")!);
        nint library = NativeLibrary.Load(file);

        unsafe
        {
            delegate* unmanaged<nint> describe = (delegate* unmanaged<nint>)NativeLibrary.GetExport(library, "KbdLayerDescriptor");

            return new WindowsLayout(describe());
        }
    }

    // What the key at a set 1 scan code types; a dead key comes back as (accent, true).
    public (char Char, bool Dead) Cell(ushort scanCode, bool shift, bool altGr, bool caps)
    {
        if (scanCode >= _scanCodes.Length || !_keys.TryGetValue((byte)_scanCodes[scanCode], out var key))
        {
            return default;
        }

        bool shifted = shift ^ (caps && (altGr ? (key.Attributes & CapsLockAltGr) != 0 : (key.Attributes & CapsLock) != 0));
        int bits = (shifted ? Shift : 0) | (altGr ? AltGr : 0);
        int column = bits < _modifierColumns.Length ? _modifierColumns[bits] : 0x0F;

        if (column >= key.Chars.Length)
        {
            return default;
        }

        ushort[] chars = caps && !altGr && key.Caps is not null ? key.Caps : key.Chars;
        ushort value = chars[column];

        return value switch
        {
            DeadMark => ((char)(key.Dead?[column] ?? 0), true),
            None or < 0x20 or 0x7F => default,
            _ => ((char)value, false),
        };
    }

    private void ReadKeys(nint tables)
    {
        for (int table = 0; Marshal.ReadIntPtr(tables, table * 16) is nint rows and not 0; table++)
        {
            int count = Marshal.ReadByte(tables, table * 16 + 8);
            int size = Marshal.ReadByte(tables, table * 16 + 9);

            for (nint row = rows; Marshal.ReadByte(row) is byte vk and not 0; row += size)
            {
                if (vk == DeadRow || _keys.ContainsKey(vk))
                {
                    continue;
                }

                byte attributes = Marshal.ReadByte(row, 1);
                ushort[] chars = Read(row, count);
                ushort[]? dead = NextIfDead(ref row, size, count, chars);
                ushort[]? caps = null;

                // An SGCAPS row comes after the main row's dead row and may have a dead row of its own.
                if ((attributes & CapsLockSecond) != 0)
                {
                    row += size;
                    caps = Read(row, count);
                    NextIfDead(ref row, size, count, caps);
                }

                _keys[vk] = (attributes, chars, caps, dead);
            }
        }
    }

    // The dead row after a row with a dead key: VK 0xFF, the accents in the dead key's columns.
    private static ushort[]? NextIfDead(ref nint row, int size, int count, ushort[] chars)
    {
        if (!chars.Contains(DeadMark) || Marshal.ReadByte(row + size) != DeadRow)
        {
            return null;
        }

        row += size;

        return Read(row, count);
    }

    private void ReadDeadKeys(nint entries)
    {
        for (nint entry = entries; entries != 0 && Marshal.ReadInt32(entry) is int both and not 0; entry += 8)
        {
            DeadKeys[((char)(both >> 16), (char)(both & 0xFFFF))] = (char)Marshal.ReadInt16(entry, 4);
        }
    }

    private static ushort[] Read(nint row, int count) =>
        [.. Enumerable.Range(0, count).Select(i => (ushort)Marshal.ReadInt16(row, 2 + i * 2))];
}
