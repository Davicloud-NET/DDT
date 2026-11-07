// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class BootEnvironmentCheckTests
{
    private static readonly SequenceStep[] s_linux =
    [
        new WriteRawImageStep { Id = Guid.NewGuid(), Name = "Write the disk", ImageId = Guid.NewGuid() },
        new WriteCloudInitSeedStep { Id = Guid.NewGuid(), Name = "Seed" },
    ];

    private static readonly SequenceStep[] s_windows = [TestRuns.Partition, TestRuns.Apply];

    [Fact]
    public void LetsARunStartInTheBootEnvironmentOfItsKind()
    {
        new BootEnvironmentCheck(AgentEnvironment.LinuxBoot).Check(s_linux);
        new BootEnvironmentCheck(AgentEnvironment.WindowsPE).Check(s_windows);

        Assert.False(new BootEnvironmentCheck(AgentEnvironment.WindowsPE).AfterDiskChoice);
    }

    [Fact]
    public void RefusesALinuxSequenceWhereWindowsPEStartedAndSaysWhichEnvironmentRunsIt()
    {
        DeploymentStepException refused = Assert.Throws<DeploymentStepException>(() => new BootEnvironmentCheck(AgentEnvironment.WindowsPE).Check(s_linux));

        Assert.Equal(
            "This sequence writes a raw disk image, which only the Linux boot environment does, and this machine started Windows PE. " +
            "Nothing was erased.",
            refused.Message);
    }

    [Fact]
    public void RefusesAWindowsSequenceWhereTheLinuxBootEnvironmentStarted()
    {
        DeploymentStepException refused = Assert.Throws<DeploymentStepException>(() => new BootEnvironmentCheck(AgentEnvironment.LinuxBoot).Check(s_windows));

        Assert.Equal(
            "This sequence installs Windows, which only Windows PE does, and this machine started the Linux boot environment. " +
            "Nothing was erased.",
            refused.Message);
    }

    // A fresh run only starts in a boot environment, so the installed Windows starts none.
    [Fact]
    public void RefusesEveryFreshRunInTheInstalledWindows()
    {
        Assert.Throws<DeploymentStepException>(() => new BootEnvironmentCheck(AgentEnvironment.Windows).Check(s_windows));
        Assert.Throws<DeploymentStepException>(() => new BootEnvironmentCheck(AgentEnvironment.Windows).Check(s_linux));
    }
}
