// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Machine;
using DDT.MachineConsole.ViewModels;
using Xunit;

namespace DDT.MachineConsole.Tests;

// The sentences that name the system the console runs on, and the keyboard layout a Linux head types by.
public sealed class LinuxHostTests
{
    [Fact]
    public void OffersAShellInTheBootEnvironment()
    {
        TestConsole console = new(system: HostSystem.Linux);

        Assert.Equal(("Shell", "Shell"), (console.Model.PromptLabel, console.Model.Machine.PromptLabel));
    }

    [Fact]
    public void ClosesInsteadOfSigningOutInTheInstalledLinux()
    {
        TestConsole console = new TestConsole(session: true, system: HostSystem.Linux).Show(Scenarios.Failed);

        Assert.Equal("Close the console", console.Model.End.CloseLabel);
    }

    [Fact]
    public void KeepsTheWindowsSentencesOnWindows()
    {
        TestConsole console = new TestConsole(session: true).Show(Scenarios.Failed);

        Assert.Equal(("Command prompt", "Sign out"), (console.Model.PromptLabel, console.Model.End.CloseLabel));
    }

    [Fact]
    public void NamesTheKeyboardLayoutOnceForEachChange()
    {
        TestConsole console = new();
        List<string?> named = [];
        console.Model.KeyboardLayoutChanged += (_, layout) => named.Add(layout);

        console.Show(Scenarios.Running).Show(Scenarios.Running);
        console.Show(Scenarios.Running with { Machine = Scenarios.Machine with { KeyboardLayout = "klingon" } });

        Assert.Equal(["German (Germany)", "klingon"], named);
    }

    [Fact]
    public void SaysWhenTheLayoutIsNotKnown()
    {
        TestConsole console = new();

        console.Model.Notice.ShowUnknownLayout("klingon");

        Assert.Equal(ConsoleNotice.UnknownKeyboardLayout, console.Model.Notice.Current);
        Assert.Equal("Unknown keyboard layout klingon. Keys type as on a US keyboard.", console.Model.Notice.Text);
    }
}
