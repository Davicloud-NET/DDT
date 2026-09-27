// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.ViewModels;
using DDT.MachineConsole.Views;
using Xunit;

namespace DDT.MachineConsole.Tests;

// The console driven the way a technician drives it, with the keyboard alone, on the real views: the field asked for
// has the focus, Enter sends, Esc goes back, the arrow keys choose, and the function keys open what is over the screen.
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

    private static MainWindow Open(TestConsole console)
    {
        MainWindow window = new(console.Model, fullScreen: false) { Width = 1024, Height = 768 };
        window.Show();
        Settle();

        return window;
    }

    // Lets what the views post, such as moving the focus, happen.
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

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Settle();
    }
}

internal static class VisualSearch
{
    // The control of that name on the screen, wherever it is in the views.
    public static T Find<T>(this Window window, string name)
        where T : Control =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<T>().FirstOrDefault(control => control.Name == name)
            ?? throw new InvalidOperationException($"No {name} on the screen.");
}
