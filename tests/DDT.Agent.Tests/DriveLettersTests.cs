// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class DriveLettersTests
{
    private const uint DrivesAtoX = (1u << 24) - 1;

    [Fact]
    public void PrefersSWAndR() =>
        Assert.Equal(new PartitionLetters('S', 'W', 'R'), DriveLetters.Choose(Letters('X', 'C', 'D')));

    [Fact]
    public void ReplacesATakenLetterWithTheHighestFreeOne()
    {
        Assert.Equal(new PartitionLetters('S', 'Z', 'R'), DriveLetters.Choose(Letters('X', 'C', 'W')));
        Assert.Equal(new PartitionLetters('Z', 'W', 'Y'), DriveLetters.Choose(Letters('X', 'C', 'S', 'R')));
        Assert.Equal(new PartitionLetters('X', 'W', 'R'), DriveLetters.Choose(Letters('C', 'S', 'Y', 'Z')));
    }

    [Fact]
    public void FailsWhenNoLetterIsLeft()
    {
        DeploymentStepException exception = Assert.Throws<DeploymentStepException>(() => DriveLetters.Choose(DrivesAtoX | Letters('Y')));

        Assert.Contains("drive letter", exception.Message, StringComparison.Ordinal);
    }

    private static uint Letters(params char[] letters) => letters.Aggregate(0u, (mask, letter) => mask | (1u << (letter - 'A')));
}
