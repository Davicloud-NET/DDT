// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.ConsoleProtocol;
using Xunit;

namespace DDT.Agent.Tests;

// The text console answers the start-over questions when the graphical one went away: a key, then the typed word.
public sealed class TextConsoleStartOverTests : IDisposable
{
    private readonly StringWriter _console = new();
    private readonly AgentLog _log;

    public TextConsoleStartOverTests() => _log = new AgentLog(new ImmediateTimeProvider(), _console);

    public void Dispose() => _console.Dispose();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AKeyWithinTheSecondsTakesTheOffer(bool pressed)
    {
        FakeKeyWait keys = new() { Pressed = pressed };
        TextMachineConsole console = new(new ScriptedSignInPrompt(), _log) { Keys = keys };

        ConsoleAnswer? answer = await console.AskAsync(new StartOverOfferQuestion(true, 10), TestContext.Current.CancellationToken);

        Assert.Equal(pressed ? new ConsoleAnswer(Continue: true) : null, answer);
        Assert.Equal([TimeSpan.FromSeconds(10)], keys.Waits);
    }

    [Fact]
    public async Task WithoutKeysTheOfferIsNeverTaken()
    {
        TextMachineConsole console = new(new ScriptedSignInPrompt(), _log);

        Assert.Null(await console.AskAsync(new StartOverOfferQuestion(false, 10), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AWithdrawnOfferIsNoAnswer()
    {
        using CancellationTokenSource withdraw = new();
        TextMachineConsole console = new(new ScriptedSignInPrompt(), _log) { Keys = new FakeKeyWait { Never = true } };

        Task<ConsoleAnswer?> asking = console.AskAsync(new StartOverOfferQuestion(true, 10), withdraw.Token);
        await withdraw.CancelAsync();

        Assert.Null(await asking);
    }

    [Fact]
    public async Task SendsTheWordAsTyped()
    {
        ScriptedSignInPrompt prompt = new("end");
        TextMachineConsole console = new(prompt, _log);

        ConsoleAnswer? answer = await console.AskAsync(new EndRunQuestion(true, "END"), TestContext.Current.CancellationToken);

        Assert.Equal(new ConsoleAnswer(Text: "end"), answer);
        Assert.Equal(["Type END to end the run and start over here"], prompt.Labels);
    }
}
