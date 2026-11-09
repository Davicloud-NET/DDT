// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Keyboard;
using Xunit;

namespace DDT.MachineConsole.Tests.Keyboard;

public sealed class EvdevEventsTests
{
    // A press of A as a keyboard sends it: the scan code, the key and the report, then a cut-off rest.
    [Fact]
    public void ReadsWholeEventsWithoutTheirTime()
    {
        byte[] read =
        [
            .. Event(0x6789, 4, 4, 0x1E), .. Event(0x6789, 1, 30, 1), .. Event(0x6789, 0, 0, 0), .. new byte[10],
        ];

        Assert.Equal(
            [
                new EvdevEvent(4, 4, 0x1E), new EvdevEvent(EvdevEvents.Key, 30, 1),
                new EvdevEvent(EvdevEvents.Sync, EvdevEvents.SyncReport, 0),
            ],
            EvdevEvents.Parse(read));
    }

    [Fact]
    public void WritesAnEventTheKernelTimes()
    {
        EvdevEvent led = new(EvdevEvents.Led, EvdevEvents.LedCapsLock, 1);
        byte[] written = EvdevEvents.Write(led);

        Assert.Equal(EvdevEvents.Size, written.Length);
        Assert.All(written[..16], value => Assert.Equal(0, value));
        Assert.Equal([led], EvdevEvents.Parse(written));
        Assert.Equal(-1, EvdevEvents.Parse(EvdevEvents.Write(led with { Value = -1 }))[0].Value);
    }

    private static byte[] Event(long seconds, ushort type, ushort code, int value)
    {
        byte[] bytes = new byte[EvdevEvents.Size];
        BitConverter.GetBytes(seconds).CopyTo(bytes, 0);
        BitConverter.GetBytes(type).CopyTo(bytes, 16);
        BitConverter.GetBytes(code).CopyTo(bytes, 18);
        BitConverter.GetBytes(value).CopyTo(bytes, 20);

        return bytes;
    }
}
