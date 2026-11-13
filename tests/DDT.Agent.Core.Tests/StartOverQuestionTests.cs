// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using Xunit;

namespace DDT.Agent.Tests;

// The question that ends a run from the machine: a key stops the countdown, and only the typed word confirms.
public sealed class StartOverQuestionTests : IDisposable
{
    private static readonly TimeSpan s_countdown = TimeSpan.FromSeconds(10);

    private readonly StringWriter _console = new();
    private readonly ImmediateTimeProvider _time = new();
    private readonly AgentLog _log;

    public StartOverQuestionTests() => _log = new AgentLog(_time, _console);

    public void Dispose() => _console.Dispose();

    private Task<bool> AskAsync(FakeKeyWait keys, ScriptedSignInPrompt prompt) =>
        new StartOverQuestion(keys, prompt, _log, _time).ConfirmedWithinAsync(s_countdown, taken: true, TestContext.Current.CancellationToken);

    [Fact]
    public async Task WithoutAKeyNothingIsAsked()
    {
        ScriptedSignInPrompt prompt = new("END");

        Assert.False(await AskAsync(new FakeKeyWait(), prompt));

        Assert.Empty(prompt.Labels);
        Assert.Empty(_console.ToString());
    }

    [Fact]
    public async Task AKeyAndTheWordConfirm()
    {
        ScriptedSignInPrompt prompt = new("END");
        FakeKeyWait keys = new() { Pressed = true };

        Assert.True(await AskAsync(keys, prompt));

        Assert.Equal([s_countdown], keys.Waits);
        Assert.Equal(["Type END to end the run and start over here"], prompt.Labels);
        Assert.Contains("WARN  A key was pressed, so the restart waits.", _console.ToString(), StringComparison.Ordinal);
    }

    // As ERASE: the word as it is written, and nothing that only looks like consent.
    [Theory]
    [InlineData("end")]
    [InlineData("End")]
    [InlineData("y")]
    [InlineData("")]
    [InlineData("END now")]
    public async Task AnythingButTheWordConfirmsNothing(string typed)
    {
        Assert.False(await AskAsync(new FakeKeyWait { Pressed = true }, new ScriptedSignInPrompt(typed)));

        Assert.Contains($"INFO  {StartOverQuestion.NotConfirmed}", _console.ToString(), StringComparison.Ordinal);
    }

    // The word is not compared without its case, so the line says what whoever typed it in small letters missed.
    [Fact]
    public async Task TheWordInSmallLettersIsToldThatItIsTypedInCapitalLetters()
    {
        Assert.False(await AskAsync(new FakeKeyWait { Pressed = true }, new ScriptedSignInPrompt("end")));

        Assert.Contains(
            "INFO  END was not typed. Only that word in capital letters ends the run, so the run goes on and the machine restarts into the installed Linux.",
            _console.ToString(),
            StringComparison.Ordinal);
    }

    // Whoever pressed the key walked away. The prompt is taken back after a minute.
    [Fact]
    public async Task NoAnswerWithinAMinuteConfirmsNothing()
    {
        ScriptedSignInPrompt prompt = new();

        Assert.False(await AskAsync(new FakeKeyWait { Pressed = true }, prompt));

        Assert.Equal(TimeSpan.FromMinutes(1), StartOverQuestion.AnswerTime);
        Assert.Contains(StartOverQuestion.AnswerTime, _time.Delays);
        Assert.Equal(1, prompt.Cancelled);
        Assert.Contains("INFO  Nobody answered within 60 seconds", _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhereNobodyCanTypeNothingIsAsked()
    {
        FakeKeyWait keys = new() { CanPress = false };
        ScriptedSignInPrompt prompt = new("END");
        StartOverQuestion question = new(keys, prompt, _log, _time);

        Assert.False(question.CanAsk);
        Assert.False(await question.ConfirmedWithinAsync(s_countdown, taken: false, TestContext.Current.CancellationToken));

        Assert.Equal([s_countdown], keys.Waits);
        Assert.Empty(prompt.Labels);
    }

    [Fact]
    public async Task AStopWhileItWaitsForTheWordIsNotAnAnswer()
    {
        using CancellationTokenSource stop = new();
        ScriptedSignInPrompt prompt = new();
        StartOverQuestion question = new(new FakeKeyWait { Pressed = true }, prompt, _log, new ManualTimeProvider());

        Task<bool> asking = question.ConfirmedWithinAsync(s_countdown, taken: true, stop.Token);
        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking);
        Assert.DoesNotContain("Nobody answered", _console.ToString(), StringComparison.Ordinal);
    }

    // The graphical console takes the keyboard, so both steps are its own screens.
    [Fact]
    public async Task OnTheGraphicalConsoleBothStepsAreItsQuestions()
    {
        FakeKeyWait keys = new() { Pressed = true };
        ScriptedSignInPrompt prompt = new("END");
        ScriptedMachineConsole screen = new(_ => new ConsoleAnswer(Continue: true), ScriptedMachineConsole.Typed("END"));
        StartOverQuestion question = new(keys, prompt, _log, _time) { Screen = screen };

        Assert.Equal(StartOverQuestion.ScreenOffer, question.Offering);
        Assert.True(await question.ConfirmedWithinAsync(s_countdown, taken: false, TestContext.Current.CancellationToken));

        Assert.Equal([new StartOverOfferQuestion(false, 10), new EndRunQuestion(false, "END")], screen.Questions);
        Assert.Empty(keys.Waits);
        Assert.Empty(prompt.Labels);
    }

    [Fact]
    public async Task AnOfferNobodyTookIsWithdrawnAndAsksNothingMore()
    {
        ScriptedMachineConsole screen = new();
        StartOverQuestion question = new(new FakeKeyWait(), new ScriptedSignInPrompt(), _log, _time) { Screen = screen };

        Assert.False(await question.ConfirmedWithinAsync(s_countdown, taken: true, TestContext.Current.CancellationToken));

        Assert.Equal([new StartOverOfferQuestion(true, 10)], screen.Questions);
        Assert.Equal(1, screen.Withdrawn);
        Assert.Contains(s_countdown, _time.Delays);
    }

    [Theory]
    [InlineData("end", false)]
    [InlineData(null, true)]
    public async Task BackOrAnotherWordOnTheScreenRestartsIntoLinux(string? typed, bool back)
    {
        ConsoleAnswer answer = back ? new ConsoleAnswer(Back: true) : new ConsoleAnswer(Text: typed);
        ScriptedMachineConsole screen = new(_ => new ConsoleAnswer(Continue: true), _ => answer);
        StartOverQuestion question = new(new FakeKeyWait(), new ScriptedSignInPrompt(), _log, _time) { Screen = screen };

        Assert.False(await question.ConfirmedWithinAsync(s_countdown, taken: true, TestContext.Current.CancellationToken));

        Assert.Contains(back ? StartOverQuestion.Declined : StartOverQuestion.NotConfirmed, _console.ToString(), StringComparison.Ordinal);
    }
}
