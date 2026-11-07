// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Server.Machines;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ElfAgentVersionTests
{
    [Fact]
    public void ReadsTheVersionTheBuildWroteIntoTheAgent() =>
        Assert.Equal(new Version(26, 1, 512), ElfAgentVersion.Of(LinuxExecutables.Versioned("26.1.512")));

    [Fact]
    public void TheLineMayEndAsWindowsEndsOne() =>
        Assert.Equal(new Version(26, 1, 512), ElfAgentVersion.Of("xxDDT-AGENT-VERSION:26.1.512\r\nyy"u8));

    [Fact]
    public void AFileWithoutTheMarkerHasNone() =>
        Assert.Null(ElfAgentVersion.Of([.. LinuxExecutables.Header(), 1, 2, 3, 4, 5, 6, 7, 8]));

    // The file passes by in blocks while it's hashed, and the marker may lie across two of them.
    [Fact]
    public void FindsAVersionCutAnywhereBetweenTwoBlocks()
    {
        byte[] executable = [1, 2, 3, .. LinuxExecutables.Marker("26.1.512"), 4, 5];

        for (int cut = 1; cut < executable.Length; cut++)
        {
            ElfAgentVersion version = new();
            version.Append(executable.AsSpan(0, cut));
            version.Append(executable.AsSpan(cut));

            Assert.Equal(new Version(26, 1, 512), version.Found);
        }
    }

    [Fact]
    public void FindsAVersionThatPassesByOneByteAtATime()
    {
        ElfAgentVersion version = new();

        foreach (byte value in LinuxExecutables.Versioned("26.1.512", size: 300))
        {
            version.Append([value]);
        }

        Assert.Equal(new Version(26, 1, 512), version.Found);
    }

    // A program that looks for the marker holds its text too, with something else after it.
    [Theory]
    [InlineData("DDT-AGENT-VERSION:\n")]
    [InlineData("DDT-AGENT-VERSION:%s\n")]
    [InlineData("DDT-AGENT-VERSION: 26.1.512\n")]
    [InlineData("DDT-AGENT-VERSION:-1.2.3\n")]
    [InlineData("DDT-AGENT-VERSION:26\n")]
    [InlineData("DDT-AGENT-VERSION:1.2.3.4.5\n")]
    [InlineData("DDT-AGENT-VERSION:11111111111111111111111111111111111111111111.1\n")]
    public void AMarkerWithoutAVersionIsPassedOver(string marker)
    {
        byte[] executable = [.. Encoding.ASCII.GetBytes(marker), 7, 7, .. LinuxExecutables.Marker("26.1.512")];

        Assert.Null(ElfAgentVersion.Of(Encoding.ASCII.GetBytes(marker)));
        Assert.Equal(new Version(26, 1, 512), ElfAgentVersion.Of(executable));
    }

    [Fact]
    public void AMarkerWhoseLineNeverEndsHasNoVersion() =>
        Assert.Null(ElfAgentVersion.Of([.. "DDT-AGENT-VERSION:26.1."u8, .. Enumerable.Repeat((byte)'7', 5000)]));

    [Fact]
    public void KeepsTheFirstVersionItFinds()
    {
        ElfAgentVersion version = new();
        version.Append(LinuxExecutables.Marker("1.0.0"));
        version.Append(LinuxExecutables.Marker("9.9.9"));

        Assert.Equal(new Version(1, 0, 0), version.Found);
    }
}
