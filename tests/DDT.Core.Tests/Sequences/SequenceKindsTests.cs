// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

public sealed class SequenceKindsTests
{
    [Fact]
    public void TakesASequenceThatWritesARawDiskImageAnywhereForALinuxSequence()
    {
        Assert.Equal(SequenceKind.Linux, SequenceKinds.Of(Definition(WriteRawImage(), WriteSeed())));
        Assert.Equal(SequenceKind.Linux, SequenceKinds.Of(Definition(Pause(), If([Group(WriteRawImage())], [Pause()]))));
        Assert.Equal(SequenceKind.Windows, SequenceKinds.Of(Definition(Partition(), ApplyImage())));

        // A seed or a shell script alone does not make one: the validator says what such a sequence lacks.
        Assert.Equal(SequenceKind.Windows, SequenceKinds.Of(Definition(WriteSeed(), Shell())));
        Assert.Equal(SequenceKind.Windows, SequenceKinds.Of(new SequenceDefinition(1, null!)));
    }

    [Theory]
    [InlineData(SequenceKind.Windows, SequencePhase.WindowsPE, AgentEnvironment.WindowsPE)]
    [InlineData(SequenceKind.Linux, SequencePhase.LinuxBoot, AgentEnvironment.LinuxBoot)]
    public void GivesEachKindItsBootPhaseAndItsBootEnvironment(SequenceKind kind, SequencePhase boot, AgentEnvironment environment)
    {
        Assert.Equal(boot, SequenceKinds.BootPhase(kind));
        Assert.Equal(environment, SequenceKinds.BootEnvironment(kind));
        Assert.True(SequenceKinds.IsBoot(boot));
        Assert.Equal(kind, SequenceKinds.Of(boot));
        Assert.Equal(kind, SequenceKinds.Of(environment));
        Assert.Equal(boot, SequenceKinds.Phase(environment));
    }

    [Theory]
    [InlineData(SequenceKind.Windows, SequencePhase.Windows, AgentEnvironment.Windows)]
    [InlineData(SequenceKind.Linux, SequencePhase.Linux, AgentEnvironment.Linux)]
    public void GivesEachKindThePhaseAndTheEnvironmentItGoesOnIn(SequenceKind kind, SequencePhase installed, AgentEnvironment environment)
    {
        Assert.Equal(installed, SequenceKinds.InstalledPhase(kind));
        Assert.Equal(environment, SequenceKinds.InstalledEnvironment(kind));
        Assert.False(SequenceKinds.IsBoot(installed));
        Assert.Equal(kind, SequenceKinds.Of(installed));
        Assert.Equal(kind, SequenceKinds.Of(environment));
        Assert.Equal(installed, SequenceKinds.Phase(environment));
    }

    // The names are stored in sequences, runs and machines, and older agents send the numbers' names.
    [Fact]
    public void KeepsTheNamesAndTheOrderOfPhasesEnvironmentsAndInterpreters()
    {
        Assert.Equal(["WindowsPE", "Windows", "LinuxBoot", "Linux"], Enum.GetNames<SequencePhase>());
        Assert.Equal(["WindowsPE", "Windows", "LinuxBoot", "Linux"], Enum.GetNames<AgentEnvironment>());
        Assert.Equal(["Cmd", "PowerShell", "Shell"], Enum.GetNames<ScriptInterpreter>());
        Assert.Equal("WaitingForCloudInit", Enum.GetNames<RunActivity>()[^1]);
        Assert.Equal(9, (int)RunActivity.WaitingForCloudInit);
    }

    // An agent of the Linux boot environment's first release says 4, so nothing of the installed Linux reaches it.
    [Fact]
    public void NeedsVersion5OnlyForAStepInTheInstalledLinux()
    {
        Assert.Equal(5, SequenceDefinition.CurrentVersion);
        Assert.Equal(5, Definition(WriteRawImage(), WriteSeed(), InLinux()).RequiredVersion());
        Assert.Equal(5, Definition(WriteRawImage(), WriteSeed(), If([Group(InLinux())])).RequiredVersion());
        Assert.Equal(4, Definition(Shell(), WriteRawImage(), WriteSeed(), Pause()).RequiredVersion());
        Assert.Equal(4, Definition(WriteRawImage()).RequiredVersion());
        Assert.Equal(1, new SequenceDefinition(1, [Partition(), ApplyImage(), Script(SequencePhase.Windows)]).RequiredVersion());
        Assert.Equal(5, Definition(WriteRawImage(), WriteSeed(), InLinux()).Normalised().Version);
        Assert.Equal(4, Definition(WriteRawImage(), WriteSeed()).Normalised().Version);
    }
}
