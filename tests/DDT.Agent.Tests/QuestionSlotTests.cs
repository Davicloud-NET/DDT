// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.ConsoleProtocol;
using Xunit;

namespace DDT.Agent.Tests;

// The one open question of a console over a pipe, as the graphical console and the console of DDT's session share it:
// what goes to a console as it comes and goes, and which answers count.
public sealed class QuestionSlotTests
{
    private static readonly PauseQuestion s_pause = new("Check the BIOS", "Set the boot order.");
    private static readonly PauseQuestion s_dock = new("Check the dock", "Plug the dock in.");

    private readonly List<ConsoleMessage> _sent = [];
    private readonly QuestionSlot _slot;

    public QuestionSlotTests()
    {
        _slot = new QuestionSlot(_sent.Add);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WaitsForAConsoleAndGoesToEachOneThatConnects()
    {
        Task<QuestionOutcome> asked = _slot.AskAsync(s_pause, Cancellation);

        Assert.Empty(_sent);
        Assert.Equal(1, _slot.OpenId);

        _slot.Connected();
        _slot.Disconnected();
        _slot.Connected();

        Assert.Equal([new QuestionMessage(1, s_pause), new QuestionMessage(1, s_pause)], _sent);
        Assert.True(_slot.Answer(1, new ConsoleAnswer(Continue: true)));
        Assert.Equal(new QuestionOutcome(new ConsoleAnswer(Continue: true), Gone: false), await asked);
        Assert.Null(_slot.OpenId);
    }

    [Fact]
    public async Task WithdrawsAQuestionTheAskerNoLongerNeedsAndIgnoresALateAnswer()
    {
        _slot.Connected();
        using CancellationTokenSource asking = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        Task<QuestionOutcome> asked = _slot.AskAsync(s_pause, asking.Token);

        await asking.CancelAsync();

        Assert.Equal(QuestionOutcome.Unanswered, await asked);
        Assert.Equal([new QuestionMessage(1, s_pause), new WithdrawMessage(1)], _sent);
        Assert.False(_slot.Answer(1, new ConsoleAnswer(Continue: true)));
    }

    // One question at a time: the one before is withdrawn, and an answer to it no longer counts.
    [Fact]
    public async Task ANewQuestionTakesThePlaceOfTheOpenOne()
    {
        _slot.Connected();
        Task<QuestionOutcome> first = _slot.AskAsync(s_pause, Cancellation);
        Task<QuestionOutcome> second = _slot.AskAsync(s_dock, Cancellation);

        Assert.Equal(QuestionOutcome.Unanswered, await first);
        Assert.Equal([new QuestionMessage(1, s_pause), new WithdrawMessage(1), new QuestionMessage(2, s_dock)], _sent);
        Assert.False(_slot.Answer(1, new ConsoleAnswer(Back: true)));
        Assert.False(_slot.Answer(7, new ConsoleAnswer(Back: true)));
        Assert.True(_slot.Answer(2, new ConsoleAnswer(Continue: true)));
        Assert.Equal(new ConsoleAnswer(Continue: true), (await second).Answer);
    }

    // As when the graphical console goes away for good: the asker asks the text console instead.
    [Fact]
    public async Task ClosingGivesEveryQuestionUpSoItCanBeAskedElsewhere()
    {
        _slot.Connected();
        Task<QuestionOutcome> asked = _slot.AskAsync(s_pause, Cancellation);

        _slot.Close();

        Assert.Equal(QuestionOutcome.NoConsole, await asked);
        Assert.Equal(QuestionOutcome.NoConsole, await _slot.AskAsync(s_dock, Cancellation));

        _slot.Connected();
        Assert.Equal([new QuestionMessage(1, s_pause)], _sent);
    }
}
