// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Input;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.Texts;
using DDT.MachineConsole.ViewModels;
using Xunit;

namespace DDT.MachineConsole.Tests;

// The console as the agent drives it, without the views.
public sealed class MainViewModelTests
{
    // The deployment setting, which the agent passes on once it has registered. What someone at the machine chose with F5
    // stands.
    [Fact]
    public void SpeaksTheLanguageTheServerNamesUnlessSomeoneChoseOne()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Running);

        Assert.Equal(UiLanguage.English, console.Model.Localizer.Language);

        console.Show(Scenarios.Running with { Language = "de" });

        Assert.Equal(UiLanguage.German, console.Model.Localizer.Language);
        Assert.Equal("Protokoll", console.Model.Keys[0].Label);

        console.Model.Press(Key.F5);
        console.Show(Scenarios.Running with { Language = "de" });

        Assert.Equal(UiLanguage.English, console.Model.Localizer.Language);
    }

    public static TheoryData<ConsoleStage, string> StageScreens => new()
    {
        { ConsoleStage.Starting, nameof(ConnectionViewModel) },
        { ConsoleStage.Connecting, nameof(ConnectionViewModel) },
        { ConsoleStage.WaitingForAuthorization, nameof(AuthorizationViewModel) },
        { ConsoleStage.WaitingForSequence, nameof(WaitingViewModel) },
        { ConsoleStage.Choosing, nameof(WaitingViewModel) },
        { ConsoleStage.Running, nameof(RunViewModel) },
        { ConsoleStage.Restarting, nameof(RunViewModel) },
        { ConsoleStage.Finished, nameof(RunViewModel) },
        { ConsoleStage.Failed, nameof(RunViewModel) },
        { ConsoleStage.Stopped, nameof(RunViewModel) },
    };

    public static TheoryData<string, string> QuestionScreens => new()
    {
        { "signIn", nameof(AuthorizationViewModel) },
        { "sequence", nameof(SequenceChoiceViewModel) },
        { "disk", nameof(DiskChoiceViewModel) },
        { "computerName", nameof(ComputerNameViewModel) },
        { "erase", nameof(EraseViewModel) },
        { "secureBoot", nameof(SecureBootViewModel) },
        { "inputs", nameof(InputsViewModel) },
        { "pause", nameof(PauseViewModel) },
    };

    [Fact]
    public void ShowsTheStartBeforeTheAgentSaysAnything()
    {
        TestConsole console = new();

        Assert.IsType<ConnectionViewModel>(console.Model.Screen);
        Assert.Equal("Waiting for the agent", console.Model.Header.Connection);
    }

    [Theory]
    [MemberData(nameof(StageScreens))]
    public void ShowsTheScreenOfEachStage(ConsoleStage stage, string screen)
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(stage));

        Assert.Equal(screen, console.Model.Screen.GetType().Name);
    }

    [Theory]
    [MemberData(nameof(QuestionScreens))]
    public void ShowsTheScreenOfEachQuestion(string kind, string screen)
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(1, Question(kind));

        Assert.Equal(screen, console.Model.Screen.GetType().Name);
    }

    [Fact]
    public void ReplacesTheWholeStateWithEachOne()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Unreachable);

        Assert.Equal("Cannot reach ddt.lab.local:8443", console.Model.Header.Connection);

        console.Show(Scenarios.State(ConsoleStage.WaitingForSequence) with { Machine = Scenarios.Machine with { Model = "OptiPlex 7010" } });

        Assert.Equal("Connected to ddt.lab.local:8443", console.Model.Header.Connection);
        Assert.Equal("Dell Inc. OptiPlex 7010", console.Model.Header.MachineLabel);
        Assert.IsType<WaitingViewModel>(console.Model.Screen);
    }

    [Fact]
    public void KeepsAScreenOfTheSameKindAndMovesIt()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Running);
        RunViewModel run = Assert.IsType<RunViewModel>(console.Model.Screen);

        console.Show(Scenarios.Running with { Run = Scenarios.Running.Run! with { Percent = 63 } });

        Assert.Same(run, console.Model.Screen);
        Assert.Equal("63", run.Percent);
    }

    [Fact]
    public void AddsLogLinesAndKeepsTheNewest()
    {
        TestConsole console = new TestConsole().Receive(new LogMessage(Scenarios.Lines), new LogMessage(Scenarios.Lines));

        Assert.Equal(2 * Scenarios.Lines.Count, console.Model.Log.Lines.Count);
        Assert.Equal(Scenarios.Lines[^1].Text, console.Model.Log.Lines[^1].Text);

        ConsoleLogLine[] many = [.. Enumerable.Range(0, LogViewModel.MaxLines).Select(index => Scenarios.Lines[0] with { Text = $"line {index}" })];
        console.Receive(new LogMessage(many));

        Assert.Equal(LogViewModel.MaxLines, console.Model.Log.Lines.Count);
        Assert.Equal($"line {LogViewModel.MaxLines - 1}", console.Model.Log.Lines[^1].Text);
    }

    [Fact]
    public void ShowsOneQuestionAtATime()
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(1, Scenarios.Sequences);

        console.Ask(2, Scenarios.Disks);

        DiskChoiceViewModel disk = Assert.IsType<DiskChoiceViewModel>(console.Model.Screen);
        Assert.Equal(2, disk.Id);
        Assert.Same(disk, console.Model.Question);
    }

    [Fact]
    public void ClosesAQuestionTheAgentWithdraws()
    {
        TestConsole console = new TestConsole()
            .Show(Scenarios.State(ConsoleStage.WaitingForAuthorization))
            .Ask(4, new SignInQuestion(SignInField.UserName, null, null));

        // Another question's withdrawal changes nothing.
        console.Receive(new WithdrawMessage(3));
        Assert.NotNull(Assert.IsType<AuthorizationViewModel>(console.Model.Screen).SignIn);

        console.Receive(new WithdrawMessage(4));

        AuthorizationViewModel waiting = Assert.IsType<AuthorizationViewModel>(console.Model.Screen);
        Assert.Null(waiting.SignIn);
        Assert.Null(console.Model.Question);
    }

    [Fact]
    public void KeepsAnAnsweredQuestionUntilTheAgentMovesOn()
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(1, Scenarios.Sequences);
        SequenceChoiceViewModel sequences = Assert.IsType<SequenceChoiceViewModel>(console.Model.Screen);

        sequences.SubmitCommand.Execute(null);

        Assert.True(sequences.IsSending);
        Assert.False(sequences.SubmitCommand.CanExecute(null));

        // A state of the same stage, such as a server problem, keeps it; the run moving on takes it away.
        console.Show(Scenarios.State(ConsoleStage.Choosing));
        Assert.Same(sequences, console.Model.Screen);

        console.Show(Scenarios.Preparing);
        Assert.IsType<RunViewModel>(console.Model.Screen);
        Assert.Null(console.Model.Question);
    }

    // Continued on the web: the agent withdraws the question, and the run screen says the run still waits until the next
    // state says it goes on.
    [Fact]
    public void ClosesThePauseWhenItWasContinuedOnTheWeb()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Paused).Ask(11, Scenarios.Pause);
        Assert.IsType<PauseViewModel>(console.Model.Screen);

        console.Receive(new WithdrawMessage(11));

        RunViewModel run = Assert.IsType<RunViewModel>(console.Model.Screen);
        Assert.Null(console.Model.Question);
        Assert.Equal(("PAUSED", TagTone.Attention), (run.Tag.Text, run.Tag.Tone));
        Assert.Equal("Paused", run.Heading);
        Assert.Equal("Check the BIOS", run.Detail);
        Assert.Empty(console.Answers);
    }

    // Enter pressed: the pause stays, its key off, until the run moves on to its next step.
    [Fact]
    public void KeepsAnAnsweredPauseUntilTheRunMovesOn()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Paused).Ask(11, Scenarios.Pause);
        PauseViewModel pause = Assert.IsType<PauseViewModel>(console.Model.Screen);

        pause.SubmitCommand.Execute(null);
        console.Show(Scenarios.Paused);

        Assert.Same(pause, console.Model.Screen);
        Assert.True(pause.IsSending);

        console.Show(Scenarios.Paused with { Run = Scenarios.Paused.Run! with { Activity = ConsoleActivity.Step } });

        Assert.IsType<RunViewModel>(console.Model.Screen);
        Assert.Null(console.Model.Question);
    }

    // The pause follows the run's state: the rail and the place on the path move with it.
    [Fact]
    public void ShowsThePauseOverTheRunAsItChanges()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Paused).Ask(11, Scenarios.Pause);
        PauseViewModel pause = Assert.IsType<PauseViewModel>(console.Model.Screen);
        ConsoleRun run = Scenarios.Paused.Run!;

        console.Show(Scenarios.Paused with { Run = run with { Steps = [.. run.Steps.Where(step => step.Name != "Inject drivers")] } });

        Assert.Equal("Pause, step 3 of 7 on this path, in Windows PE", pause.Position);
        Assert.Equal(7, pause.Steps.Count);
    }

    [Fact]
    public void SaysWhyNothingRunsWhileTheRunWaitsForItsInputs()
    {
        TestConsole console = new TestConsole().Show(Scenarios.WaitingForInputs);
        RunViewModel run = Assert.IsType<RunViewModel>(console.Model.Screen);

        Assert.Equal(("WAITING", TagTone.Attention), (run.Tag.Text, run.Tag.Tone));
        Assert.Equal("Waiting for answers", run.Heading);
        Assert.Equal("Nothing runs until the sequence's inputs are answered, here or on the machine's page on the web.", run.Note);

        console.Model.Press(Key.F5);

        Assert.Equal("Warten auf Antworten", run.Heading);
        Assert.Equal("WARTET", run.Tag.Text);
    }

    // Of a tree, the rail shows the leaves on the run's path in order: no group, IF or repeat node, and not the step of
    // the branch the IF did not take.
    [Fact]
    public void ShowsTheStepsOnTheRunsPath()
    {
        TestConsole console = new TestConsole().Show(Scenarios.RunningTree);
        RunViewModel run = Assert.IsType<RunViewModel>(console.Model.Screen);

        Assert.Equal(
            ["Partition the disk", "Apply the Latitude image", "Inject drivers", "Check the BIOS", "Write the answer file", "Restart into Windows", "Join the domain", "Run script: baseline"],
            run.Steps.Select(step => step.Name));
        Assert.Equal(["01", "02"], run.Steps.Take(2).Select(step => step.Number));
        Assert.Equal("Step 2 of 8 on this path, in Windows PE", run.Position);
        Assert.Equal([6, 2], run.Phases.Select(phase => phase.Steps));
        Assert.Equal("37", run.Percent);

        // Before the IF has decided, both of its branches are still to come.
        ConsoleRun undecided = Scenarios.RunningTree.Run! with
        {
            Steps = [.. Scenarios.TreeSteps.Select(step => step with { State = ConsoleStepState.Pending, Pass = 0, Branch = null })],
            CurrentStepId = null,
        };
        console.Show(Scenarios.RunningTree with { Run = undecided });

        Assert.Equal(9, run.Steps.Count);
        Assert.Equal("0 of 9 steps done", run.Position);
    }

    [Fact]
    public void TakesTheSignInsNextFieldInPlace()
    {
        TestConsole console = new TestConsole()
            .Show(Scenarios.State(ConsoleStage.WaitingForAuthorization))
            .Ask(1, new SignInQuestion(SignInField.UserName, null, null));
        SignInViewModel signIn = Assert.IsType<SignInViewModel>(console.Model.Question);
        signIn.UserName = "anna";
        signIn.SubmitCommand.Execute(null);

        console.Ask(2, new SignInQuestion(SignInField.Password, "anna", null));

        Assert.Same(signIn, console.Model.Question);
        Assert.Equal(2, signIn.Id);
        Assert.True(signIn.AsksPassword);
        Assert.False(signIn.IsSending);
        Assert.Equal([(1, new ConsoleAnswer(Text: "anna"))], console.Answers);
    }

    [Fact]
    public void KeepsTheLastStateWhenThePipeEnds()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Running).Ask(9, Scenarios.Erase);

        console.Model.Ended(LinkEnd.Closed);

        RunViewModel run = Assert.IsType<RunViewModel>(console.Model.Screen);
        Assert.Equal("62", run.Percent);
        Assert.Null(console.Model.Question);
        Assert.True(console.Model.End.IsEnded);
        Assert.Equal("The agent has ended", console.Model.Header.Connection);

        // Nothing that might still arrive changes it.
        console.Show(Scenarios.Finished).Ask(10, Scenarios.Sequences);
        Assert.Same(run, console.Model.Screen);
        Assert.Equal("62", run.Percent);
    }

    [Fact]
    public void SaysWhenThePipeBroke()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Running);

        console.Model.Ended(LinkEnd.Broken);

        Assert.Equal("The connection to the agent broke", console.Model.End.EndedTitle);
    }

    [Fact]
    public void RestartsOnlyInWindowsPEOnceTheAgentHasEndedAndOnlyWhenAsked()
    {
        TestConsole desktop = new TestConsole(canRestart: false).Show(Scenarios.Finished);
        desktop.Model.Ended(LinkEnd.Closed);

        Assert.False(desktop.Model.Press(Key.F8));
        Assert.False(desktop.Model.End.RestartCommand.CanExecute(null));
        Assert.Equal(0, desktop.Power.Restarts);

        TestConsole windowsPE = new TestConsole(canRestart: true).Show(Scenarios.Finished);
        Assert.False(windowsPE.Model.Press(Key.F8));

        windowsPE.Model.Ended(LinkEnd.Closed);
        Assert.True(windowsPE.Model.Press(Key.F8));
        Assert.True(windowsPE.Model.End.ConfirmingRestart);
        Assert.Equal(0, windowsPE.Power.Restarts);

        Assert.True(windowsPE.Model.Press(Key.Escape));
        Assert.False(windowsPE.Model.End.ConfirmingRestart);
        Assert.Equal(0, windowsPE.Power.Restarts);

        windowsPE.Model.Press(Key.F8);
        Assert.True(windowsPE.Model.Press(Key.Enter));
        Assert.Equal(1, windowsPE.Power.Restarts);
    }

    [Fact]
    public void ClosesOnlyOnceTheAgentHasEnded()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Running);

        Assert.False(console.Model.Press(Key.F9));
        Assert.False(console.Model.End.CloseCommand.CanExecute(null));

        console.Model.Ended(LinkEnd.Closed);

        Assert.True(console.Model.Press(Key.F9));
        Assert.Equal(1, console.Closed);
    }

    [Fact]
    public void OpensTheCommandPromptOnShiftF10WhateverTheConsoleShows()
    {
        TestConsole console = new(canRestart: true);

        // Before the agent has said anything.
        Assert.True(console.Model.Press(Key.F10, KeyModifiers.Shift));

        // Over a question with the log open, which both stay.
        console.Show(Scenarios.State(ConsoleStage.Choosing)).Ask(4, Scenarios.Sequences);
        console.Model.Press(Key.F1);
        Assert.True(console.Model.Press(Key.F10, KeyModifiers.Shift));
        Assert.Equal(Overlay.Log, console.Model.OverlayShown);
        Assert.IsType<SequenceChoiceViewModel>(console.Model.Screen);

        // Once the agent has ended, even while the restart is asked about, which is still asked.
        console.Model.Ended(LinkEnd.Closed);
        console.Model.Press(Key.F8);
        Assert.True(console.Model.Press(Key.F10, KeyModifiers.Shift));
        Assert.True(console.Model.End.ConfirmingRestart);

        // And from the keys that show it.
        console.Model.OpenPromptCommand.Execute(null);
        console.Model.Machine.PromptCommand!.Execute(null);

        Assert.Equal(5, console.Prompt.Opened);
        Assert.Equal(0, console.Power.Restarts);
        Assert.Equal(0, console.Closed);
        Assert.Empty(console.Answers);
    }

    [Fact]
    public void OpensTheCommandPromptOnlyWithShiftAndTakesNoOtherKeyWithAModifier()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Running);

        Assert.False(console.Model.Press(Key.F10));
        Assert.False(console.Model.Press(Key.F10, KeyModifiers.Shift | KeyModifiers.Control));
        Assert.False(console.Model.Press(Key.F10, KeyModifiers.Alt));
        Assert.False(console.Model.Press(Key.F1, KeyModifiers.Shift));

        Assert.Equal(0, console.Prompt.Opened);
        Assert.False(console.Model.HasOverlay);
        Assert.Equal("Command prompt", console.Model.PromptLabel);
        Assert.Equal("Eingabeaufforderung", new TestConsole(UiLanguage.German).Model.PromptLabel);
    }

    // A laptop's top row sends mute and volume without Fn. The console says how to reach F1 to F12, and stops saying it
    // once one of them comes through.
    [Fact]
    public void SaysToHoldFnWhenATopRowSendsMediaKeys()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Running);

        Assert.False(console.Model.Notice.IsShown);
        Assert.True(console.Model.Press(Key.VolumeMute));
        Assert.Equal(ConsoleNotice.MediaKeys, console.Model.Notice.Current);
        Assert.False(console.Model.HasOverlay);
        Assert.Equal(["Fn"], console.Model.Notice.Keys);
        Assert.Contains("Hold Fn", console.Model.Notice.Text, StringComparison.Ordinal);

        Assert.True(console.Model.Press(Key.F2));
        Assert.False(console.Model.Notice.IsShown);
        Assert.Equal(Overlay.Machine, console.Model.OverlayShown);

        console.Model.Press(Key.VolumeUp);
        Assert.True(console.Model.Notice.IsShown);
        console.Model.Press(Key.Escape);
        Assert.False(console.Model.Notice.IsShown);

        TestConsole german = new(UiLanguage.German);
        german.Model.Press(Key.VolumeDown);
        Assert.StartsWith("Die obere Tastenreihe", german.Model.Notice.Text, StringComparison.Ordinal);
    }

    // Alt+F4 while the agent works would leave the machine to the text console, and passers-by press it. The console
    // stays and says how to reach a prompt; once the agent has ended, closing is F9's job and goes through.
    [Fact]
    public void RefusesToCloseWhileTheAgentWorksAndSaysHowToReachAPrompt()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Running);

        Assert.True(console.Model.RefuseClose());
        Assert.Equal(ConsoleNotice.CloseRefused, console.Model.Notice.Current);
        Assert.Equal(["Shift", "F10"], console.Model.Notice.Keys);
        Assert.Equal(
            "The console stays open while DDT works on this machine. Shift+F10 opens a command prompt.",
            console.Model.Notice.Text);

        // The next key has read it; the note leaves and keeps its words while it fades.
        console.Model.Press(Key.F1);
        Assert.False(console.Model.Notice.IsShown);
        Assert.Equal(["Shift", "F10"], console.Model.Notice.Keys);
        Assert.Equal(0, console.Closed);

        // Before the agent has said anything, too.
        Assert.True(new TestConsole().Model.RefuseClose());

        console.Model.Ended(LinkEnd.Closed);
        Assert.False(console.Model.RefuseClose());

        TestConsole german = new(UiLanguage.German);
        Assert.True(german.Model.RefuseClose());
        Assert.StartsWith("Die Konsole bleibt offen", german.Model.Notice.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void OpensAndClosesTheLogTheMachineAndTheLicencesOnTheirKeys()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Running);

        Assert.True(console.Model.Press(Key.F1));
        Assert.Same(console.Model.Log, console.Model.OverlayContent);
        Assert.True(console.Model.Keys[0].IsActive);

        Assert.True(console.Model.Press(Key.F2));
        Assert.Same(console.Model.Machine, console.Model.OverlayContent);

        Assert.True(console.Model.Press(Key.F2));
        Assert.False(console.Model.HasOverlay);

        Assert.True(console.Model.Press(Key.F3));
        Assert.IsType<LicencesViewModel>(console.Model.OverlayContent);
        Assert.True(console.Model.Press(Key.Escape));
        Assert.False(console.Model.HasOverlay);

        // Esc with nothing open is the screen's own.
        Assert.False(console.Model.Press(Key.Escape));
    }

    [Fact]
    public void ClosesWhatIsOverTheScreenForANewQuestionButNotForTheNextField()
    {
        TestConsole console = new TestConsole()
            .Show(Scenarios.State(ConsoleStage.WaitingForAuthorization))
            .Ask(1, new SignInQuestion(SignInField.UserName, null, null));

        console.Model.Press(Key.F2);
        console.Ask(2, new SignInQuestion(SignInField.Password, "anna", null));
        Assert.Equal(Overlay.Machine, console.Model.OverlayShown);

        console.Show(Scenarios.State(ConsoleStage.Choosing)).Ask(3, Scenarios.Sequences);
        Assert.Equal(Overlay.None, console.Model.OverlayShown);
    }

    [Fact]
    public void SwitchesTheThemeAndTheLanguage()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Running);

        Assert.True(console.Model.IsDark);
        Assert.Equal("Light", console.Model.Keys[3].Label);

        console.Model.Press(Key.F4);

        Assert.False(console.Model.IsDark);
        Assert.Equal("Dark", console.Model.Keys[3].Label);

        Assert.Equal("Deutsch", console.Model.Keys[4].Label);
        console.Model.Press(Key.F5);

        Assert.Equal(UiLanguage.German, console.Model.Localizer.Language);
        Assert.Equal("Protokoll", console.Model.Keys[0].Label);
        Assert.Equal("English", console.Model.Keys[4].Label);
        Assert.Equal("Image wird angewendet", Assert.IsType<RunViewModel>(console.Model.Screen).Heading);
    }

    [Fact]
    public void ShowsTheSignInBesideWhatTellsTheMachineApart()
    {
        TestConsole console = new TestConsole()
            .Show(Scenarios.State(ConsoleStage.WaitingForAuthorization))
            .Ask(1, new SignInQuestion(SignInField.Password, "anna", "Wrong user name or password."));

        AuthorizationViewModel authorization = Assert.IsType<AuthorizationViewModel>(console.Model.Screen);
        SignInViewModel signIn = Assert.IsType<SignInViewModel>(authorization.SignIn);

        Assert.Equal("Wrong user name or password.", signIn.Error);
        Assert.Equal("German (Germany)", signIn.KeyboardLayout);
        Assert.Contains(authorization.Identity, fact => fact.Value == "3C:52:82:6A:1F:0B");
        Assert.Contains(authorization.Identity, fact => fact.Value == "7XK2Q34");
    }

    [Fact]
    public void SaysWhoSignedInWhileAnOperatorStillHasToApprove()
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.WaitingForAuthorization) with { SignedInBy = "anna" });

        AuthorizationViewModel authorization = Assert.IsType<AuthorizationViewModel>(console.Model.Screen);

        Assert.StartsWith("anna signed in here.", authorization.Intro, StringComparison.Ordinal);
        Assert.False(authorization.HasSignIn);
    }

    internal static ConsoleQuestion Question(string kind) => kind switch
    {
        "signIn" => new SignInQuestion(SignInField.UserName, null, null),
        "sequence" => Scenarios.Sequences,
        "disk" => Scenarios.Disks,
        "computerName" => Scenarios.ComputerName(),
        "erase" => Scenarios.Erase,
        "inputs" => Scenarios.Inputs(),
        "pause" => Scenarios.Pause,
        _ => Scenarios.SecureBoot,
    };
}
