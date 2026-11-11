// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.Texts;
using DDT.MachineConsole.ViewModels;
using Xunit;

namespace DDT.MachineConsole.Tests;

// The console as the shell of DDT's session over a real named pipe. A stand-in agent asks, goes away and comes back,
// like the agent's service in the installed Windows.
public sealed class SessionLinkTests : IAsyncDisposable
{
    private static readonly ConsoleAnswer s_continue = new(Continue: true);

    private readonly string _pipeName = ConsolePipe.NewName();
    private readonly CancellationTokenSource _stop = new();
    private readonly Pump _ui = new();
    private readonly NamedPipeServerStream _pipe;
    private readonly ConsoleChannel _agent;
    private readonly SessionLink _link;
    private readonly MainViewModel _model;
    private readonly Task _serving;

    public SessionLinkTests()
    {
        _pipe = ConsolePipe.CreateServer(_pipeName);
        _agent = new ConsoleChannel(_pipe);
        _link = new SessionLink(ConnectAsync, _ui.Post, TimeSpan.FromMilliseconds(20));
        _model = new MainViewModel(
            Localizer.Embedded(UiLanguage.English),
            new FakePower(false),
            new FakePrompt(),
            _link.Answer,
            () => { },
            session: true);
        _serving = Task.Run(() => _link.ServeAsync(_model, _stop.Token));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        // A connected link only ends with its pipe, so the agent's end closes before the wait.
        await _stop.CancelAsync();
        _agent.Dispose();
        await _pipe.DisposeAsync();
        await _serving.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        _stop.Dispose();
    }

    // A Pause step in the installed Windows. Enter at the machine continues the run, like the web does.
    [Fact]
    public async Task ContinuingAPauseAtTheMachineReachesTheAgent()
    {
        await TheAgentStartsAsync();
        PauseViewModel pause = await PausesAsync(1);

        pause.SubmitCommand.Execute(null);

        Assert.Equal(new AnswerMessage(1, s_continue), await TheAgentGetsAsync());
        Assert.True(pause.IsSending);
    }

    [Fact]
    public async Task TheAnswersToARunsInputsReachTheAgent()
    {
        InputsQuestion question = new("Windows 11 24H2 with Office", [Scenarios.Inputs().Inputs[1]], null);
        await TheAgentStartsAsync();
        await _agent.SendAsync(new StateMessage(Scenarios.WaitingForInputs), Cancellation);
        await _agent.SendAsync(new QuestionMessage(4, question), Cancellation);
        InputsViewModel inputs = await _ui.UntilAsync(() => _model.Question as InputsViewModel);

        Assert.IsType<TextFieldViewModel>(inputs.Fields[0]).Text = "anna.berger";
        inputs.SubmitCommand.Execute(null);

        AnswerMessage answer = Assert.IsType<AnswerMessage>(await TheAgentGetsAsync());
        Assert.Equal(4, answer.Id);
        Assert.Equal([new ConsoleInputValue("Owner", "anna.berger")], answer.Answer.Values);
    }

    // The agent's service restarts while the pause waits. It asks again, and the answer goes out on the new connection.
    [Fact]
    public async Task AnAgentThatComesBackGetsTheAnswerToWhatItAsksAgain()
    {
        await TheAgentStartsAsync();
        await PausesAsync(1);
        await TheAgentStopsAsync();

        Assert.Null(_model.Question);

        await TheAgentStartsAsync();
        PauseViewModel pause = await PausesAsync(1);
        pause.SubmitCommand.Execute(null);

        Assert.Equal(new AnswerMessage(1, s_continue), await TheAgentGetsAsync());
    }

    // Enter on a pause that is still leaving the screen. The agent that comes back only gets the answer to its own
    // question.
    [Fact]
    public async Task AnAnswerGivenWhileTheAgentIsAwayGoesNowhere()
    {
        await TheAgentStartsAsync();
        PauseViewModel left = await PausesAsync(1);
        await TheAgentStopsAsync();

        left.SubmitCommand.Execute(null);

        await TheAgentStartsAsync();
        PauseViewModel pause = await PausesAsync(2);
        pause.SubmitCommand.Execute(null);

        Assert.Equal(new AnswerMessage(2, s_continue), await TheAgentGetsAsync());
    }

    // A restarted agent numbers its questions from 1 again, so the agent can't tell an old answer by its number.
    [Fact]
    public async Task AnAnswerToWhatOnlyAnEarlierAgentAskedIsNotSent()
    {
        await TheAgentStartsAsync();
        await PausesAsync(7);
        await TheAgentStopsAsync();
        await TheAgentStartsAsync();
        PauseViewModel pause = await PausesAsync(1);

        _link.Answer(7, s_continue);
        pause.SubmitCommand.Execute(null);

        Assert.Equal(new AnswerMessage(1, s_continue), await TheAgentGetsAsync());
    }

    // One agent comes and goes before the UI thread gets to it. Only the agent connected now is shown and answered.
    [Fact]
    public async Task AnAgentThatCameAndWentWhileTheScreenWasBusyIsNotShown()
    {
        await TheAgentStartsAsync();
        await _agent.SendAsync(new StateMessage(Scenarios.Paused), Cancellation);
        await _agent.SendAsync(new QuestionMessage(1, Scenarios.Pause), Cancellation);
        _pipe.WaitForPipeDrain();
        _pipe.Disconnect();
        await TheAgentStartsAsync();
        await _ui.BusyUntilAsync(waiting: 2);

        PauseViewModel pause = await PausesAsync(2);
        pause.SubmitCommand.Execute(null);

        Assert.False(_model.Header.IsDetached);
        Assert.Equal(new AnswerMessage(2, s_continue), await TheAgentGetsAsync());
    }

    private async Task<IAgentConnection> ConnectAsync(CancellationToken stop) =>
        new ClientConnection(await ConsoleClient.ConnectAsync(_pipeName, "DDT console test", TimeSpan.FromSeconds(10), stop));

    // The agent's service is up. The console connects by itself, and both say hello.
    private async Task TheAgentStartsAsync()
    {
        await _pipe.WaitForConnectionAsync(Cancellation);
        Assert.IsType<HelloMessage>(await _agent.ReceiveAsync(Cancellation));
        await _agent.SendAsync(new HelloMessage(HelloMessage.CurrentVersion, "DDT agent 1.4.0"), Cancellation);
    }

    // The agent's service is gone, and the screen says so.
    private async Task TheAgentStopsAsync()
    {
        _pipe.Disconnect();
        await _ui.UntilAsync(() => _model.Header.IsDetached ? _model : null);
    }

    // What the agent sends while a Pause step waits. Returns once that pause is on the screen.
    private async Task<PauseViewModel> PausesAsync(int id)
    {
        await _agent.SendAsync(new StateMessage(Scenarios.Paused), Cancellation);
        await _agent.SendAsync(new QuestionMessage(id, Scenarios.Pause), Cancellation);

        return await _ui.UntilAsync(() => _model.Question is PauseViewModel pause && pause.Id == id ? pause : null);
    }

    // The next message the console sends.
    private async Task<ConsoleMessage?> TheAgentGetsAsync()
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            return await _agent.ReceiveAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!Cancellation.IsCancellationRequested)
        {
            throw new TimeoutException("No answer reached the agent within 10 s.");
        }
    }
}
