// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Keyboard;
using Xunit;

namespace DDT.MachineConsole.Tests.Keyboard;

public sealed class EvdevBitsTests
{
    // A keyboard reports key events, Enter and letters; a power button only its key, a numeric pad digits and Enter.
    [Theory]
    [InlineData(new[] { 28, 30, 31 }, true)]
    [InlineData(new[] { 28, 2, 3 }, true)]
    [InlineData(new[] { 116 }, false)]
    [InlineData(new[] { 30, 31 }, false)]
    public void TakesOnlyKeyboards(int[] keys, bool keyboard)
    {
        byte[] types = Bits(EvdevEvents.Sync, EvdevEvents.Key, EvdevBits.Repeat);

        Assert.Equal(keyboard, EvdevBits.IsKeyboard(types, Bits(keys)));
        Assert.False(EvdevBits.IsKeyboard(Bits(EvdevEvents.Sync), Bits(28, 30)));
    }

    [Fact]
    public void ListsTheKeysABitSetHolds()
    {
        Assert.Equal([(ushort)29, (ushort)56, (ushort)111], EvdevBits.Codes(Bits(29, 56, 111)));
        Assert.Empty(EvdevBits.Codes(new byte[96]));
        Assert.False(EvdevBits.IsSet(new byte[2], 700));
    }

    private static byte[] Bits(params int[] set)
    {
        byte[] bits = new byte[96];

        foreach (int bit in set)
        {
            bits[bit / 8] |= (byte)(1 << (bit % 8));
        }

        return bits;
    }
}
