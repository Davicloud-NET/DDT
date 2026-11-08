// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;
using DDT.MachineConsole.ViewModels;
using Xunit;

namespace DDT.MachineConsole.Tests;

// A run of a Linux sequence says Linux where a Windows run says Windows.
public sealed class LinuxRunTests
{
    [Fact]
    public void NamesTheLinuxBootEnvironmentAndTheInstalledLinux()
    {
        TestConsole console = new TestConsole().Show(Scenarios.RunningLinux);
        RunViewModel run = Assert.IsType<RunViewModel>(console.Model.Screen);

        Assert.Equal("Writing the disk image", run.Heading);
        Assert.Equal("Step 1 of 5, in the Linux boot environment", run.Position);
        Assert.Equal("Leave this machine on. It restarts by itself and finishes in Linux.", run.Note);
        Assert.Equal(["In the Linux boot environment", "In the installed Linux"], run.Phases.Select(phase => phase.Label));
        Assert.Equal([2, 3], run.Phases.Select(phase => phase.Steps));

        console.Show(Scenarios.RunningInLinux);
        run = Assert.IsType<RunViewModel>(console.Model.Screen);

        Assert.Equal("Step 3 of 5, in the installed Linux", run.Position);
        Assert.Equal("Leave this machine on. This screen says when the run is done.", run.Note);
    }

    [Fact]
    public void SaysItInGerman()
    {
        TestConsole console = new TestConsole(UiLanguage.German).Show(Scenarios.RunningLinux);
        RunViewModel run = Assert.IsType<RunViewModel>(console.Model.Screen);

        Assert.Equal("Schritt 1 von 5, in der Linux-Startumgebung", run.Position);
        Assert.Equal("Lassen Sie dieses Gerät eingeschaltet. Es startet von selbst neu und wird in Linux fertig.", run.Note);

        console.Show(Scenarios.WaitingForCloudInit);
        Assert.Equal("Wartet auf cloud-init", Assert.IsType<RunViewModel>(console.Model.Screen).Heading);

        console.Show(Scenarios.RestartingIntoLinux);
        Assert.Equal("Die Ausführung geht im installierten Linux weiter.", Assert.IsType<RunViewModel>(console.Model.Screen).Detail);
    }

    [Fact]
    public void HandsTheRunOverToTheInstalledLinux()
    {
        TestConsole console = new TestConsole().Show(Scenarios.HandingOverToLinux);

        Assert.Equal("Handing the run over to the installed Linux", Assert.IsType<RunViewModel>(console.Model.Screen).Heading);

        console.Show(Scenarios.RestartingIntoLinux);
        RunViewModel run = Assert.IsType<RunViewModel>(console.Model.Screen);

        Assert.Equal("Restarting into the installed system", run.Heading);
        Assert.Equal("The run goes on in the installed Linux.", run.Detail);

        console.Show(Scenarios.WaitingForCloudInit);

        Assert.Equal("Waiting for cloud-init", Assert.IsType<RunViewModel>(console.Model.Screen).Heading);
    }

    [Fact]
    public void RestartsIntoTheLinuxBootEnvironment()
    {
        TestConsole console = new TestConsole().Show(Scenarios.RestartingIntoLinuxBoot);
        RunViewModel run = Assert.IsType<RunViewModel>(console.Model.Screen);

        Assert.Equal("Restarting into the Linux boot environment", run.Heading);
        Assert.Equal("A step asked for a restart. The run goes on after it.", run.Detail);
    }

    [Fact]
    public void ShowsWhereInTheInstalledLinuxItPauses()
    {
        TestConsole console = new TestConsole().Show(Scenarios.PausedInLinux).Ask(12, Scenarios.Pause);
        PauseViewModel pause = Assert.IsType<PauseViewModel>(console.Model.Screen);

        Assert.Equal("Pause, step 4 of 5 on this path, in the installed Linux", pause.Position);
    }

    // A Windows run says what it said before the Linux siblings came.
    [Fact]
    public void LeavesAWindowsRunsWordsAsTheyWere()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Restarting);
        RunViewModel run = Assert.IsType<RunViewModel>(console.Model.Screen);

        Assert.Equal("Restarting into the installed system", run.Heading);
        Assert.Equal("The run goes on in the installed Windows.", run.Detail);

        console.Show(Scenarios.State(ConsoleStage.Running) with
        {
            Run = Scenarios.Run([.. Enumerable.Repeat(ConsoleStepState.Done, 5), .. Enumerable.Repeat(ConsoleStepState.Pending, 3)], null, null, ConsoleActivity.HandingOver),
        });

        Assert.Equal("Handing the run over to the installed Windows", Assert.IsType<RunViewModel>(console.Model.Screen).Heading);
        Assert.Equal(["In Windows PE", "In the installed Windows"], Assert.IsType<RunViewModel>(console.Model.Screen).Phases.Select(phase => phase.Label));
    }
}
