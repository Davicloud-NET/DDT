// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.BitLocker;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// A boot image without manage-bde, where only a Pre-provision BitLocker step set to fail stops the run before it starts.
public sealed class BootImageToolsCheckTests : IDisposable
{
    private readonly string _system = Path.Combine(Path.GetTempPath(), $"ddt-system-{Guid.NewGuid():N}");

    public BootImageToolsCheckTests() => Directory.CreateDirectory(_system);

    public void Dispose() => Directory.Delete(_system, recursive: true);

    private static PreprovisionBitLockerStep Preprovision(BitLockerUnavailable ifUnavailable) =>
        new() { Id = Guid.NewGuid(), Name = "Pre-provision BitLocker", IfUnavailable = ifUnavailable };

    [Fact]
    public void APreprovisionSetToFailNeedsManageBde()
    {
        DeploymentStepException failure = Assert.Throws<DeploymentStepException>(() => new BootImageToolsCheck(_system).Check([Preprovision(BitLockerUnavailable.Fail)]));

        Assert.Equal(PreprovisionBitLockerStepRunner.NoManageBde, failure.Message);
    }

    [Fact]
    public void APreprovisionSetToSkipGoesOnWithoutIt() =>
        new BootImageToolsCheck(_system).Check([Preprovision(BitLockerUnavailable.Skip)]);

    [Fact]
    public void ManageBdeInTheBootImageIsEnough()
    {
        File.WriteAllBytes(ManageBdeBitLocker.In(_system), []);

        new BootImageToolsCheck(_system).Check([Preprovision(BitLockerUnavailable.Fail)]);
    }
}
