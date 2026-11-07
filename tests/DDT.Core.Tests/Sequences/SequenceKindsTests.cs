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
    [InlineData(SequenceKind.Windows, SequencePhase.WindowsPE, AgentEnvironment.WindowsPE, SequencePhase.Windows)]
    [InlineData(SequenceKind.Linux, SequencePhase.LinuxBoot, AgentEnvironment.LinuxBoot, null)]
    public void GivesEachKindItsBootPhaseItsBootEnvironmentAndWhereItGoesOn(
        SequenceKind kind,
        SequencePhase boot,
        AgentEnvironment environment,
        SequencePhase? installed)
    {
        Assert.Equal(boot, SequenceKinds.BootPhase(kind));
        Assert.Equal(environment, SequenceKinds.BootEnvironment(kind));
        Assert.Equal(installed, SequenceKinds.InstalledPhase(kind));
        Assert.True(SequenceKinds.IsBoot(boot));
        Assert.Equal(kind, SequenceKinds.Of(boot));
        Assert.Equal(boot, SequenceKinds.Phase(environment));
    }

    [Fact]
    public void TakesTheInstalledWindowsForNoBootPhase()
    {
        Assert.False(SequenceKinds.IsBoot(SequencePhase.Windows));
        Assert.Equal(SequenceKind.Windows, SequenceKinds.Of(SequencePhase.Windows));
        Assert.Equal(SequencePhase.Windows, SequenceKinds.Phase(AgentEnvironment.Windows));
    }

    // The names are stored in sequences, runs and machines, and older agents send the numbers' names.
    [Fact]
    public void KeepsTheNamesAndTheOrderOfPhasesEnvironmentsAndInterpreters()
    {
        Assert.Equal(["WindowsPE", "Windows", "LinuxBoot"], Enum.GetNames<SequencePhase>());
        Assert.Equal(["WindowsPE", "Windows", "LinuxBoot"], Enum.GetNames<AgentEnvironment>());
        Assert.Equal(["Cmd", "PowerShell", "Shell"], Enum.GetNames<ScriptInterpreter>());
    }
}
