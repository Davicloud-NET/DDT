// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.ViewModels;
using DDT.MachineConsole.Views;
using Xunit;

namespace DDT.MachineConsole.Tests;

// Drives the console on the real views with the keyboard only, like a technician does.
public sealed class KeyboardTests
{
    [Fact]
    public Task SignsInWithTheKeyboardAlone() => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole()
            .Show(Scenarios.State(ConsoleStage.WaitingForAuthorization))
            .Ask(1, new SignInQuestion(SignInField.UserName, null, null));
        MainWindow window = Open(console);

        Type(window, "anna");
        Press(window, Key.Enter);
        console.Ask(2, new SignInQuestion(SignInField.Password, "anna", null));
        Settle();

        TextBox password = window.Find<TextBox>("PasswordBox");
        Assert.True(password.IsFocused);
        Assert.Equal('•', password.PasswordChar);

        Type(window, "correct horse");
        Assert.Equal("correct horse", password.Text);
        Press(window, Key.Enter);

        Assert.Equal([(1, new ConsoleAnswer(Text: "anna")), (2, new ConsoleAnswer(Text: "correct horse"))], console.Answers);
        Assert.Equal(string.Empty, password.Text);
        window.Close();
    });

    [Fact]
    public Task TypesIntoTheFieldAgainAfterTheLogClosed() => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole()
            .Show(Scenarios.State(ConsoleStage.WaitingForAuthorization))
            .Ask(1, new SignInQuestion(SignInField.UserName, null, null));
        MainWindow window = Open(console);

        Press(window, Key.F1);
        Press(window, Key.Escape);
        Type(window, "anna");
        Press(window, Key.Enter);

        Assert.Equal([(1, new ConsoleAnswer(Text: "anna"))], console.Answers);
        window.Close();
    });

    [Fact]
    public Task GoesBackFromThePasswordWithEsc() => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole()
            .Show(Scenarios.State(ConsoleStage.WaitingForAuthorization))
            .Ask(2, new SignInQuestion(SignInField.Password, "anna", null));
        MainWindow window = Open(console);

        Press(window, Key.Escape);

        Assert.Equal([(2, new ConsoleAnswer(Text: string.Empty))], console.Answers);
        window.Close();
    });

    [Fact]
    public Task ChoosesASequenceWithTheArrowKeys() => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(4, Scenarios.Sequences);
        MainWindow window = Open(console);

        Press(window, Key.Down);
        Press(window, Key.Down);
        Press(window, Key.Enter);

        Assert.Equal([(4, new ConsoleAnswer(SequenceId: Scenarios.Sequences.Sequences[2].Id))], console.Answers);
        window.Close();
    });

    [Fact]
    public Task PicksNoDiskWithEnterAloneAndGoesBackWithEsc() => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(5, Scenarios.Disks);
        MainWindow window = Open(console);

        Press(window, Key.Enter);
        Assert.Empty(console.Answers);

        Press(window, Key.Escape);
        Assert.Equal([(5, new ConsoleAnswer(Back: true))], console.Answers);
        window.Close();
    });

    [Fact]
    public Task ErasesOnlyOnceTheWordIsTyped() => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(7, Scenarios.Erase);
        MainWindow window = Open(console);
        TextBox word = window.Find<TextBox>("WordBox");

        Assert.True(word.IsFocused);
        Type(window, "erase");
        Press(window, Key.Enter);
        Assert.Empty(console.Answers);

        word.Text = string.Empty;
        Type(window, "ERASE");
        Press(window, Key.Enter);

        Assert.Equal([(7, new ConsoleAnswer(Text: "ERASE"))], console.Answers);
        window.Close();
    });

    // The first field has the focus. Tab moves from field to field, the arrow keys choose, and Enter sends them all.
    [Fact]
    public Task AnswersTheInputsWithTheKeyboardAlone() => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(9, Scenarios.Inputs());
        MainWindow window = Open(console);

        Press(window, Key.Right);
        Press(window, Key.Tab);
        Type(window, "anna.berger");
        Press(window, Key.Tab);
        Press(window, Key.Right);
        Press(window, Key.Tab);
        Type(window, @"LAB\svc-join");
        Press(window, Key.Tab);
        Type(window, "correct horse");
        TextBox password = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<TextBox>().Single(box => box.PasswordChar == '•');
        Assert.True(password.IsFocused);
        Press(window, Key.Enter);

        (int id, ConsoleAnswer answer) = Assert.Single(console.Answers);
        Assert.Equal(9, id);
        Assert.Equal(
            [
                new ConsoleInputValue("Office", "ProPlus"),
                new ConsoleInputValue("Owner", "anna.berger"),
                new ConsoleInputValue("BitLocker", "false"),
                new ConsoleInputValue("JoinAccount", null, @"LAB\svc-join", "correct horse"),
            ],
            answer.Values);
        Assert.Equal(string.Empty, password.Text);
        window.Close();
    });

    // Typing filters a long list, Down enters it, Space ticks, Up leaves it, typing in it goes on in the search.
    [Fact]
    public Task SearchesALongListAndTicksWithTheKeyboardAlone() => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(13, Scenarios.Applications);
        MainWindow window = Open(console);
        TextBox search = window.Find<TextBox>("SearchBox");
        ListBox list = window.Find<ListBox>("ChoicesList");

        Assert.True(search.IsFocused);
        Type(window, "micro");
        Assert.Equal(2, list.ItemCount);

        Press(window, Key.Down);
        Press(window, Key.Down);
        Press(window, Key.Space);
        Press(window, Key.Up);
        Press(window, Key.Up);
        Assert.True(search.IsFocused);

        Press(window, Key.Down);
        Type(window, "soft t");
        Assert.True(search.IsFocused);
        Assert.Equal("microsoft t", search.Text);
        Press(window, Key.Enter);

        (int id, ConsoleAnswer answer) = Assert.Single(console.Answers);
        Assert.Equal(13, id);
        Assert.Equal(new ConsoleInputValue("Apps", "app-7zip;app-m365;app-teams"), answer.Values?[0]);
        window.Close();
    });

    [Fact]
    public Task SendsNoInputsWithEnterUntilTheyAreAnsweredAndGoesBackWithEsc() => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(9, Scenarios.Inputs());
        MainWindow window = Open(console);

        Press(window, Key.Enter);
        Assert.Empty(console.Answers);

        Press(window, Key.Escape);
        Assert.Equal([(9, new ConsoleAnswer(Back: true))], console.Answers);
        window.Close();
    });

    [Fact]
    public Task ContinuesAPauseWithEnter() => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.Paused).Ask(11, Scenarios.Pause);
        MainWindow window = Open(console);

        Press(window, Key.Escape);
        Assert.Empty(console.Answers);

        Press(window, Key.Enter);

        Assert.Equal([(11, new ConsoleAnswer(Continue: true))], console.Answers);
        window.Close();
    });

    [Fact]
    public Task OpensTheLogOverTheScreenAndClosesItBeforeEscGoesBack() => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(5, Scenarios.Disks);
        MainWindow window = Open(console);

        Press(window, Key.F1);
        Assert.Equal(Overlay.Log, console.Model.OverlayShown);

        Press(window, Key.Escape);
        Assert.Equal(Overlay.None, console.Model.OverlayShown);
        Assert.Empty(console.Answers);

        Press(window, Key.Escape);
        Assert.Equal([(5, new ConsoleAnswer(Back: true))], console.Answers);
        window.Close();
    });

    [Fact]
    public Task OpensTheCommandPromptWithShiftF10AndTypesOnInTheField() => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole()
            .Show(Scenarios.State(ConsoleStage.WaitingForAuthorization))
            .Ask(1, new SignInQuestion(SignInField.UserName, null, null));
        MainWindow window = Open(console);

        Press(window, Key.F10, RawInputModifiers.Shift);
        Assert.Equal(1, console.Prompt.Opened);

        // F10 alone does nothing, and the field still takes what is typed.
        Press(window, Key.F10);
        Type(window, "anna");
        Press(window, Key.Enter);

        Assert.Equal(1, console.Prompt.Opened);
        Assert.Equal([(1, new ConsoleAnswer(Text: "anna"))], console.Answers);
        window.Close();
    });

    [Fact]
    public Task ShowsThePromptKeyInTheMachinesDetailsAndOnceTheAgentHasEnded() => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.Running);
        MainWindow window = Open(console);

        Assert.False(window.Find<Button>("EndedPrompt").IsEffectivelyVisible);

        Press(window, Key.F2);
        Button details = window.Find<Button>("Prompt");
        Assert.True(details.IsEffectivelyVisible);
        details.Command!.Execute(null);

        Press(window, Key.Escape);
        console.Model.Ended(Agent.LinkEnd.Closed);
        Settle();
        Button ended = window.Find<Button>("EndedPrompt");
        Assert.True(ended.IsEffectivelyVisible);
        ended.Command!.Execute(null);

        Assert.Equal(2, console.Prompt.Opened);
        window.Close();
    });

    [Fact]
    public Task FillsTheScreenWithoutStayingInFrontOfOtherWindows() => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.Running);
        MainWindow window = new(console.Model, fullScreen: true);
        window.Show();
        Settle();

        Assert.Equal(WindowState.FullScreen, window.WindowState);
        Assert.False(window.Topmost);
        window.Close();
    });

    // A key of a short tick list shows the focus ring once the keyboard reaches it, outside its edge as on the other
    // keys.
    [Fact]
    public Task TheTickKeyWithTheFocusShowsTheFocusRing() => Headless.RunAsync(() =>
    {
        TestConsole console = new TestConsole().Show(Scenarios.State(ConsoleStage.Choosing)).Ask(10, Scenarios.Inputs(refused: true));
        MainWindow window = Open(console);
        ToggleButton german = window.GetVisualDescendants().OfType<ToggleButton>().First();
        Color before = PixelBeside(window, german);

        Press(window, Key.Tab);

        Assert.True(german.IsFocused);
        Color ring = ((ISolidColorBrush)german.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "focus").BorderBrush!).Color;
        Assert.False(IsNear(before, ring), $"{before} is already the ring's {ring}.");
        Assert.True(IsNear(PixelBeside(window, german), ring), $"{PixelBeside(window, german)} is not the ring's {ring}.");
        window.Close();
    });
    private static MainWindow Open(TestConsole console)
    {
        MainWindow window = new(console.Model, fullScreen: false) { Width = 1024, Height = 768 };
        window.Show();
        Settle();

        return window;
    }

    // Runs what the views posted, like moving the focus.
    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Type(Window window, string text)
    {
        window.KeyTextInput(text);
        Settle();
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Settle();
    }

    // The pixel 3 px left of the key's middle, inside the ring drawn 2 to 4 px outside its edge.
    private static Color PixelBeside(Window window, Control key)
    {
        Point at = key.TranslatePoint(new Point(-3, key.Bounds.Height / 2), window) ?? throw new InvalidOperationException("The key is not in the window.");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was drawn.");
        using ILockedFramebuffer buffer = frame.Lock();
        byte[] pixel = BitConverter.GetBytes(Marshal.ReadInt32(buffer.Address, ((int)at.Y * buffer.RowBytes) + ((int)at.X * 4)));

        return buffer.Format == PixelFormat.Rgba8888
            ? Color.FromArgb(pixel[3], pixel[0], pixel[1], pixel[2])
            : Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static bool IsNear(Color drawn, Color expected) =>
        Math.Abs(drawn.R - expected.R) + Math.Abs(drawn.G - expected.G) + Math.Abs(drawn.B - expected.B) < 40;
}
