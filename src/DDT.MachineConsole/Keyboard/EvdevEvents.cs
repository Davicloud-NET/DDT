// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;

namespace DDT.MachineConsole.Keyboard;

// The input_event of 64-bit Linux: two 8-byte time fields, then type, code and value.
public static class EvdevEvents
{
    public const int Size = 24;

    public const ushort Sync = 0;
    public const ushort Key = 1;
    public const ushort Led = 0x11;

    public const ushort SyncReport = 0;
    public const ushort SyncDropped = 3;

    public const ushort LedNumLock = 0;
    public const ushort LedCapsLock = 1;

    // The whole events in what one read gave. A read from evdev never splits one.
    public static IReadOnlyList<EvdevEvent> Parse(ReadOnlySpan<byte> bytes)
    {
        List<EvdevEvent> events = new(bytes.Length / Size);

        for (int at = 0; at + Size <= bytes.Length; at += Size)
        {
            events.Add(new EvdevEvent(
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[(at + 16)..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[(at + 18)..]),
                BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 20)..])));
        }

        return events;
    }

    // An event to write to a device, like a LED. The kernel sets the time.
    public static byte[] Write(EvdevEvent input)
    {
        byte[] bytes = new byte[Size];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), input.Type);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18), input.Code);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), input.Value);

        return bytes;
    }
}
