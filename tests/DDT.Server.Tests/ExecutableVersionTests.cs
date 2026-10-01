// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Machines;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ExecutableVersionTests
{
    [Fact]
    public void ReadsTheFileVersionOfAnExecutable() =>
        Assert.Equal(new Version(0, 4, 12, 0), ExecutableVersion.Of(Executables.Versioned("0.4.12")));

    [Fact]
    public void AFileWithoutAVersionResourceHasNone() =>
        Assert.Null(ExecutableVersion.Of([(byte)'M', (byte)'Z', 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18]));

    // The file passes by in blocks while it's hashed, and the version may lie across two of them.
    [Fact]
    public void FindsAVersionCutAnywhereBetweenTwoBlocks()
    {
        byte[] executable = [1, 2, 3, .. Executables.FixedFileInfo(new Version(2, 7, 1, 9)), 4, 5];

        for (int cut = 1; cut < executable.Length; cut++)
        {
            ExecutableVersion version = new();
            version.Append(executable.AsSpan(0, cut));
            version.Append(executable.AsSpan(cut));

            Assert.Equal(new Version(2, 7, 1, 9), version.Found);
        }
    }

    [Fact]
    public void KeepsTheFirstVersionItFinds()
    {
        ExecutableVersion version = new();
        version.Append(Executables.FixedFileInfo(new Version(1, 0, 0, 0)));
        version.Append(Executables.FixedFileInfo(new Version(9, 9, 9, 9)));

        Assert.Equal(new Version(1, 0, 0, 0), version.Found);
    }
}
