// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Input;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.ViewModels;
using Xunit;

namespace DDT.MachineConsole.Tests;

// The console as the shell of DDT's session, with an agent that disappears and comes back as its service restarts.
public sealed class SessionTests
{
    [Fact]
    public void OpensNoCommandPrompt()
    {
        TestConsole console = new(session: true);
        console.Show(Scenarios.Running);

        Assert.True(console.Model.Press(Key.F10, KeyModifiers.Shift));

        Assert.Equal(0, console.Prompt.Opened);
        Assert.False(console.Model.OpenPromptCommand.CanExecute(null));
        Assert.False(console.Model.CanOpenPrompt);
        Assert.False(console.Model.Machine.HasPrompt);
    }

    [Fact]
    public void NeverClosesAndSignsOutWithF9OnceTheRunIsOver()
    {
        TestConsole console = new TestConsole(session: true).Show(Scenarios.Running);

        Assert.True(console.Model.RefuseClose());
        Assert.Equal(ConsoleNotice.SessionCloseRefused, console.Model.Notice.Current);
        Assert.False(console.Model.Press(Key.F9));
        Assert.False(console.Model.End.ShowsEndBand);

        console.Show(Scenarios.Failed);

        Assert.True(console.Model.End.ShowsEndBand);
        Assert.Equal("Sign out", console.Model.End.CloseLabel);
        Assert.True(console.Model.RefuseClose());
        Assert.Equal(ConsoleNotice.SignOutWithF9, console.Model.Notice.Current);
        Assert.True(console.Model.Press(Key.F9));
        Assert.Equal(1, console.Closed);
    }

    [Fact]
    public void WaitsForTheAgentWhenItGoesAndStartsItsLogAfreshWhenItComesBack()
    {
        TestConsole console = new TestConsole(session: true).Show(Scenarios.Running);
        console.Receive(new LogMessage(Scenarios.Lines));

        console.Model.Detached();

        Assert.Equal("Waiting for the agent", console.Model.Header.Connection);
        Assert.False(console.Model.End.IsEnded);
        Assert.IsType<RunViewModel>(console.Model.Screen);

        // The agent sends its newest lines again when it reconnects.
        console.Model.Attached();
        Assert.Empty(console.Model.Log.Lines);
        console.Show(Scenarios.Running).Receive(new LogMessage(Scenarios.Lines));

        Assert.Equal(Scenarios.Lines.Count, console.Model.Log.Lines.Count);
        Assert.StartsWith("Connected to", console.Model.Header.Connection, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysWhatSigningOutLeadsTo()
    {
        TestConsole console = new TestConsole(session: true).Show(Scenarios.Stopped);

        Assert.Equal("DDT is done with this machine", console.Model.End.EndedTitle);
        Assert.Equal("Sign out to leave DDT's session. Windows then shows its sign-in screen.", console.Model.End.EndedText);
        Assert.False(console.Model.End.ShowsRestartUnavailable);
    }

    [Fact]
    public void InWindowsPEThePipeEndingStillEndsTheConsole()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Failed);

        Assert.False(console.Model.End.ShowsEndBand);

        console.Model.Ended(LinkEnd.Closed);

        Assert.True(console.Model.End.ShowsEndBand);
        Assert.Equal("Close the console", console.Model.End.CloseLabel);
        Assert.True(console.Model.CanOpenPrompt);
    }

    [Fact]
    public void InWindowsTheRunNoLongerHasTheMoveIntoWindowsAhead()
    {
        ConsoleStepState[] inWindowsPE = [ConsoleStepState.Done, ConsoleStepState.Running, .. Enumerable.Repeat(ConsoleStepState.Pending, 6)];
        ConsoleStepState[] inWindows = [.. Enumerable.Repeat(ConsoleStepState.Done, 6), ConsoleStepState.Running, ConsoleStepState.Pending];
        TestConsole console = new TestConsole(session: true)
            .Show(Scenarios.State(ConsoleStage.Running) with { Run = Scenarios.Run(inWindowsPE, 1, 62) });

        Assert.Equal("Leave this machine on. It restarts by itself and finishes in Windows.", Assert.IsType<RunViewModel>(console.Model.Screen).Note);

        console.Show(Scenarios.State(ConsoleStage.Running) with { Run = Scenarios.Run(inWindows, 6, null) });

        Assert.Equal("Leave this machine on. This screen says when the run is done.", Assert.IsType<RunViewModel>(console.Model.Screen).Note);
    }

    [Fact]
    public void TheArgumentSaysWhichConsoleItIs()
    {
        Assert.True(ConsolePipe.IsSession(["--pipe", "ddt-console-00", "--session"]));
        Assert.False(ConsolePipe.IsSession(["--pipe", "ddt-console-00"]));
    }
}
