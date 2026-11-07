// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Machines;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ElfHeaderTests
{
    private const ushort MachineArm64 = 183;
    private const ushort Relocatable = 1;
    private const ushort CoreDump = 4;

    [Fact]
    public void AProgramForX64IsOne()
    {
        Assert.True(ElfHeader.IsX64Program(LinuxExecutables.Header()));

        // Linked for a fixed address, as a program that is not position-independent is
        Assert.True(ElfHeader.IsX64Program(LinuxExecutables.Header(type: 2)));
    }

    [Fact]
    public void AProgramForAnotherProcessorOrWith32BitsIsNot()
    {
        Assert.False(ElfHeader.IsX64Program(LinuxExecutables.Header(machine: MachineArm64)));
        Assert.False(ElfHeader.IsX64Program(LinuxExecutables.Header(bits: 1, machine: 3)));
        Assert.False(ElfHeader.IsX64Program(LinuxExecutables.Header(bits: 1)));
    }

    [Fact]
    public void AnObjectFileAndACoreDumpAreNoPrograms()
    {
        Assert.False(ElfHeader.IsX64Program(LinuxExecutables.Header(type: Relocatable)));
        Assert.False(ElfHeader.IsX64Program(LinuxExecutables.Header(type: CoreDump)));
    }

    [Fact]
    public void AnythingElseIsNot()
    {
        byte[] bigEndian = LinuxExecutables.Header();
        bigEndian[5] = 2;

        Assert.False(ElfHeader.IsX64Program(bigEndian));
        Assert.False(ElfHeader.IsX64Program(LinuxExecutables.Header()[..19]));
        Assert.False(ElfHeader.IsX64Program([(byte)'M', (byte)'Z', .. new byte[18]]));
        Assert.False(ElfHeader.IsX64Program("#!/bin/sh\necho not an agent\n"u8));
        Assert.False(ElfHeader.IsX64Program([]));
    }
}
