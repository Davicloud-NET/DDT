// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class DiskpartScriptTests
{
    [Fact]
    public void CleansADiskForARawImageWithoutMakingAnyPartition()
    {
        Assert.Equal("select disk 3\r\nclean\r\nexit\r\n", DiskpartScript.Clean(3));
    }

    [Fact]
    public void BuildsMicrosoftsUefiLayoutWithCrlfAndNoBlankLine()
    {
        string script = DiskpartScript.Build(2, new PartitionLetters('S', 'W', 'R'));

        Assert.Equal(
            "select disk 2\r\n" +
            "clean\r\n" +
            "convert gpt\r\n" +
            "create partition efi size=300\r\n" +
            "format quick fs=fat32 label=\"System\"\r\n" +
            "assign letter=S\r\n" +
            "create partition msr size=16\r\n" +
            "create partition primary\r\n" +
            "shrink minimum=1024\r\n" +
            "format quick fs=ntfs label=\"Windows\"\r\n" +
            "assign letter=W\r\n" +
            "create partition primary\r\n" +
            "format quick fs=ntfs label=\"Recovery\"\r\n" +
            "assign letter=R\r\n" +
            "set id=\"de94bba4-06d1-4d40-a16a-bfd50179d6ac\"\r\n" +
            "gpt attributes=0x8000000000000001\r\n" +
            "exit\r\n",
            script);
        Assert.DoesNotContain("\r\n\r\n", script, StringComparison.Ordinal);
        Assert.All(script, character => Assert.True(character < 128));
    }

    [Fact]
    public void GivesARunsPartitionsLettersAgain()
    {
        string script = DiskpartScript.AssignLetters(1, 1, 'S', 4, 'R');

        Assert.Equal(
            "select disk 1\r\n" +
            "select partition 1\r\n" +
            "assign letter=S\r\n" +
            "select partition 4\r\n" +
            "assign letter=R\r\n" +
            "exit\r\n",
            script);
    }

    [Fact]
    public void UsesThePartitionSizesItIsGiven()
    {
        string script = DiskpartScript.Build(0, new PartitionLetters('S', 'W', 'R'), 260, 65536);

        Assert.Contains("create partition efi size=260\r\n", script, StringComparison.Ordinal);
        Assert.Contains("shrink minimum=65536\r\n", script, StringComparison.Ordinal);
    }

    [Fact]
    public void UsesTheLettersItIsGiven() =>
        Assert.Contains("assign letter=Z\r\n", DiskpartScript.Build(0, new PartitionLetters('Z', 'Y', 'X')), StringComparison.Ordinal);
}
