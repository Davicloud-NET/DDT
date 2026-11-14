// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsUpdates;
using Xunit;

namespace DDT.Agent.Tests;

// One update's own result code, as the spike's aborted and finished jobs answered.
public sealed class OperationResultsTests
{
    [Theory]
    [InlineData(OperationResults.Succeeded)]
    [InlineData(OperationResults.SucceededWithErrors)]
    public void SucceededIsSucceededAndKeepsTheRestart(int code)
    {
        Assert.Equal(new UpdateOutcome("a", UpdateResult.Succeeded, 0, true), OperationResults.Outcome("a", code, 0x00240003, restartNeeded: true));
    }

    [Fact]
    public void AFailedUpdateCarriesItsHResult()
    {
        Assert.Equal(new UpdateOutcome("a", UpdateResult.Failed, unchecked((int)0x80240022)), OperationResults.Outcome("a", OperationResults.Failed, unchecked((int)0x80240022), restartNeeded: false));
    }

    // The spike's aborted download: the cumulative update was 5 with 0x8024000B.
    [Theory]
    [InlineData(OperationResults.Aborted)]
    [InlineData(OperationResults.NotStarted)]
    [InlineData(OperationResults.InProgress)]
    public void AnUpdateTheJobDidNotFinishIsAborted(int code)
    {
        Assert.Equal(new UpdateOutcome("a", UpdateResult.Aborted), OperationResults.Outcome("a", code, unchecked((int)0x8024000B), restartNeeded: false));
    }
}
