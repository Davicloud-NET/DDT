// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole.Keyboard;

// The bit sets an evdev device reports: which event types, keys or LEDs it has, or which keys it holds now.
public static class EvdevBits
{
    public const int Repeat = 0x14;

    private const int Enter = 28;
    private const int A = 30;
    private const int One = 2;

    public static bool IsSet(ReadOnlySpan<byte> bits, int bit) => bit / 8 < bits.Length && (bits[bit / 8] & (1 << (bit % 8))) != 0;

    // A keyboard has Enter and letters or digits. Power buttons, lid switches and mice stay with the kernel.
    public static bool IsKeyboard(ReadOnlySpan<byte> types, ReadOnlySpan<byte> keys) =>
        IsSet(types, EvdevEvents.Key) && IsSet(keys, Enter) && (IsSet(keys, A) || IsSet(keys, One));

    public static IReadOnlyCollection<ushort> Codes(ReadOnlySpan<byte> bits)
    {
        List<ushort> codes = [];

        for (int bit = 0; bit < bits.Length * 8; bit++)
        {
            if (IsSet(bits, bit))
            {
                codes.Add((ushort)bit);
            }
        }

        return codes;
    }
}
