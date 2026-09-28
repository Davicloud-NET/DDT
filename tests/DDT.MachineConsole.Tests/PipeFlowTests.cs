// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using System.IO.Pipes;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.Texts;
using DDT.MachineConsole.ViewModels;
using Xunit;

namespace DDT.MachineConsole.Tests;

// The console over a real named pipe, with a stand-in for the agent that speaks the protocol as the agent does.
public sealed class PipeFlowTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GoesThroughASignInAndARunUntilTheAgentEnds()
    {
        string pipeName = ConsolePipe.NewName();
        await using NamedPipeServerStream pipe = ConsolePipe.CreateServer(pipeName);
        using ConsoleChannel agent = new(pipe);

        Task<ClientConnection> connecting = ClientConnection.ConnectAsync(pipeName, "DDT console test", Cancellation);
        await pipe.WaitForConnectionAsync(Cancellation);

        // The console says hello in the agent's version, and the agent accepts it.
        HelloMessage hello = Assert.IsType<HelloMessage>(await agent.ReceiveAsync(Cancellation));
        Assert.Equal(HelloMessage.CurrentVersion, hello.Version);
        Assert.Equal("DDT console test", hello.Program);
        await agent.SendAsync(new HelloMessage(HelloMessage.CurrentVersion, "DDT agent 1.4.0"), Cancellation);

        Pump ui = new();
        await using AgentLink link = new(await connecting);
        MainViewModel model = new(
            Localizer.Embedded(UiLanguage.English),
            new FakePower(false),
            new FakePrompt(),
            (id, answer) => _ = link.AnswerAsync(id, answer),
            () => { });
        Inbox inbox = new(ui.Post);
        inbox.Deliver(model.Receive, model.Ended);
        Task reading = Task.Run(async () => inbox.End(await link.ReadAsync(inbox.Add)), Cancellation);

        // What the agent sends first: its state, the lines from before, and the open question.
        await agent.SendAsync(new StateMessage(Scenarios.State(ConsoleStage.WaitingForAuthorization)), Cancellation);
        await agent.SendAsync(new LogMessage(Scenarios.Lines), Cancellation);
        await agent.SendAsync(new QuestionMessage(1, new SignInQuestion(SignInField.UserName, null, null)), Cancellation);

        SignInViewModel signIn = await ui.UntilAsync(() => model.Question as SignInViewModel);
        Assert.Equal(Scenarios.Lines.Count, model.Log.Lines.Count);

        // The answer goes back over the pipe, in the field the question names.
        signIn.UserName = "anna";
        signIn.SubmitCommand.Execute(null);
        Assert.Equal(new AnswerMessage(1, new ConsoleAnswer(Text: "anna")), await agent.ReceiveAsync(Cancellation));

        // An approval on the web takes the next question away.
        await agent.SendAsync(new QuestionMessage(2, new SignInQuestion(SignInField.Password, "anna", null)), Cancellation);
        await ui.UntilAsync(() => signIn.AsksPassword ? signIn : null);
        await agent.SendAsync(new WithdrawMessage(2), Cancellation);
        await ui.UntilAsync(() => model.Question is null ? model : null);

        await agent.SendAsync(new StateMessage(Scenarios.Running), Cancellation);
        RunViewModel run = await ui.UntilAsync(() => model.Screen as RunViewModel);
        Assert.Equal("62", run.Percent);

        // The agent ends: the pipe closes, and the last state stays.
        pipe.Disconnect();
        await ui.UntilAsync(() => model.End.IsEnded ? model : null);
        await reading;

        Assert.Same(run, model.Screen);
        Assert.Equal("The agent has ended", model.End.EndedTitle);
    }

    [Fact]
    public async Task EndsAtOnceWhenTheAgentRefusesIt()
    {
        string pipeName = ConsolePipe.NewName();
        await using NamedPipeServerStream pipe = ConsolePipe.CreateServer(pipeName);
        using ConsoleChannel agent = new(pipe);

        Task<int> console = Task.Run(() => Program.Main([ConsolePipe.PipeArgument, pipeName]), Cancellation);
        await pipe.WaitForConnectionAsync(Cancellation);
        Assert.IsType<HelloMessage>(await agent.ReceiveAsync(Cancellation));
        await agent.SendAsync(new RefusedMessage(HelloMessage.CurrentVersion + 1, "This agent speaks version 2 of the console protocol, not 1."), Cancellation);

        Assert.Equal(Program.Refused, await console.WaitAsync(TimeSpan.FromSeconds(10), Cancellation));
    }

    [Fact]
    public void EndsAtOnceWithoutAPipe()
    {
        Assert.Equal(Program.NoPipe, Program.Main([]));
        Assert.Equal(Program.NoPipe, Program.Main(["--pipe", @"\\.\pipe\not-the-agents"]));
    }

    // The UI thread of a test: what the inbox posts runs here, in order, while the test waits for what it expects.
    private sealed class Pump
    {
        private readonly ConcurrentQueue<Action> _posted = new();

        public void Post(Action action) => _posted.Enqueue(action);

        public async Task<T> UntilAsync<T>(Func<T?> found)
            where T : class
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);

            while (true)
            {
                while (_posted.TryDequeue(out Action? action))
                {
                    action();
                }

                if (found() is { } value)
                {
                    return value;
                }

                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("The console did not get there within 10 s.");
                }

                await Task.Delay(10, Cancellation);
            }
        }
    }
}
