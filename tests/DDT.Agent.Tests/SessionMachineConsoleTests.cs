// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using DDT.Agent.Consoles;
using DDT.ConsoleProtocol;
using Xunit;

namespace DDT.Agent.Tests;

// The console of DDT's session over a real named pipe, with consoles in this process that connect, leave and come
// back, as the session's shell does while Windows and the agent's service restart.
public sealed class SessionMachineConsoleTests : IAsyncDisposable
{
    private const string Password = "Tr0ub4dor&3-join";

    private static readonly PauseQuestion s_pause = new("Check the BIOS", "Set the boot order to the network first.");

    private readonly StringWriter _writer = new();
    private readonly string _pipe = ConsolePipe.NewName();
    private readonly SessionMachineConsole _console;
    private readonly AgentLog _log;
    private int _connected;

    public SessionMachineConsoleTests()
    {
        _log = new AgentLog(TimeProvider.System, _writer);
        _console = new SessionMachineConsole(_log, "1.0.0", () => Interlocked.Increment(ref _connected));
        _log.MachineConsole = _console;
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static SecurityIdentifier CurrentUser => WindowsIdentity.GetCurrent().User!;

    public async ValueTask DisposeAsync()
    {
        await _console.DisposeAsync();
        _writer.Dispose();
    }

    [Fact]
    public async Task EachConsoleThatConnectsGetsTheStateAndTheNewestLines()
    {
        ConsoleStatus status = new(_console, "1.0.0", new Uri("https://ddt.test:8443/"), null, dryRun: false);
        _log.Information("Written before any console connected.");
        _console.Start(() => SessionMachineConsole.CreatePipe(_pipe, CurrentUser));

        await using (ConsoleClient first = await ConnectAsync())
        {
            (ConsoleState state, List<string> lines) = await ReadAsync(first, "The console of DDT's session, Test console, is connected.");

            Assert.Equal(ConsoleStage.Starting, state.Stage);
            Assert.Equal(["Written before any console connected.", "The console of DDT's session, Test console, is connected."], lines);

            status.Registering();
            _log.Information("Written while it was connected.");
            (state, lines) = await ReadAsync(first, "Written while it was connected.");
            Assert.Equal(ConsoleStage.Connecting, state.Stage);
        }

        // Another, after the first left: the whole state again, and the lines from before it came.
        await using ConsoleClient second = await ConnectAsync();
        (ConsoleState again, List<string> backlog) = await ReadAsync(second, "Written while it was connected.");

        Assert.Equal(ConsoleStage.Connecting, again.Stage);
        Assert.Contains("Written before any console connected.", backlog);
        Assert.Equal(2, Volatile.Read(ref _connected));
        Assert.True(_console.CanAsk);
    }

    [Fact]
    public async Task KeepsTheNewest500LinesForAConsoleThatComesLate()
    {
        for (int line = 1; line <= 600; line++)
        {
            _log.Information($"Line {line}.");
        }

        _console.Start(() => SessionMachineConsole.CreatePipe(_pipe, CurrentUser));
        await using ConsoleClient client = await ConnectAsync();
        (_, List<string> lines) = await ReadAsync(client, "The console of DDT's session, Test console, is connected.", withState: false);

        // The line that says it connected is the newest.
        Assert.Equal(500, lines.Count);
        Assert.Equal("Line 102.", lines[0]);
        Assert.Equal("Line 600.", lines[^2]);
    }

    [Fact]
    public async Task RefusesAConsoleOfAnotherVersionAndServesTheNextOne()
    {
        _console.Start(() => SessionMachineConsole.CreatePipe(_pipe, CurrentUser));

        await Assert.ThrowsAsync<ConsoleProtocolException>(
            () => ConnectAsync(new HelloMessage(HelloMessage.CurrentVersion + 1, "Future console")));

        await using ConsoleClient client = await ConnectAsync();
        Assert.Equal($"DDT agent 1.0.0", client.Agent.Program);
    }

    [Fact]
    public async Task ThePipeIsTheAgentsAndOpensOnlyToTheSessionsAccount()
    {
        SecurityIdentifier session = new("S-1-5-21-1111111111-2222222222-3333333333-1005");
        await using NamedPipeServerStream pipe = SessionMachineConsole.CreatePipe(_pipe, session);

        PipeSecurity security = pipe.GetAccessControl();
        List<PipeAccessRule> rules = [.. security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>()];

        Assert.Equal(CurrentUser, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.Equal(2, rules.Count);
        Assert.All(rules, rule => Assert.Equal(AccessControlType.Allow, rule.AccessControlType));
        Assert.Equal(PipeAccessRights.FullControl, Rights(rules, CurrentUser));
        Assert.Equal(PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, Rights(rules, session));
    }

    private static PipeAccessRights Rights(List<PipeAccessRule> rules, SecurityIdentifier sid) =>
        rules.Single(rule => rule.IdentityReference == sid).PipeAccessRights | PipeAccessRights.Synchronize;

    [Fact]
    public async Task TheLastStateGoesOutBeforeThePipeCloses()
    {
        ConsoleStatus status = new(_console, "1.0.0", new Uri("https://ddt.test:8443/"), null, dryRun: false);
        _console.Start(() => SessionMachineConsole.CreatePipe(_pipe, CurrentUser));
        await using ConsoleClient client = await ConnectAsync();
        await ReadAsync(client, "The console of DDT's session, Test console, is connected.");

        // Reading all the while, as the console does.
        Task<ConsoleState?> reading = Task.Run(
            async () =>
            {
                ConsoleState? last = null;

                while (await client.ReceiveAsync(Cancellation) is { } message)
                {
                    if (message is StateMessage state)
                    {
                        last = state.State;
                    }
                }

                return last;
            },
            Cancellation);

        status.RunFailed("The script ended with exit code 1.");
        await _console.DisposeAsync();

        Assert.Equal(ConsoleStage.Failed, (await reading)?.Stage);
    }

    // A Pause step asks while Windows restarts the session: the question waits, and each console that connects gets it
    // after the state and the lines, until one answers.
    [Fact]
    public async Task AsksEachConsoleThatConnectsAfterTheStateAndTheLinesUntilOneAnswers()
    {
        _ = new ConsoleStatus(_console, "1.0.0", new Uri("https://ddt.test:8443/"), null, dryRun: false);
        _log.Information("Written before the pause.");
        Task<ConsoleAnswer?> asked = _console.AskAsync(s_pause, Cancellation);

        Assert.False(_console.CanAsk);

        _console.Start(() => SessionMachineConsole.CreatePipe(_pipe, CurrentUser));
        int id;

        await using (ConsoleClient first = await ConnectAsync())
        {
            (QuestionMessage question, List<ConsoleMessage> before) = await UntilAsync<QuestionMessage>(first);

            Assert.Equal(s_pause, question.Question);
            Assert.IsType<StateMessage>(before[0]);
            Assert.Contains(before, message => message is LogMessage log && log.Lines.Any(line => line.Text == "Written before the pause."));
            Assert.True(_console.CanAsk);
            id = question.Id;
        }

        // Nobody answered before the session restarted.
        await using ConsoleClient second = await ConnectAsync();
        (QuestionMessage again, List<ConsoleMessage> beforeAgain) = await UntilAsync<QuestionMessage>(second);

        Assert.Equal(new QuestionMessage(id, s_pause), again);
        Assert.IsType<StateMessage>(beforeAgain[0]);
        Assert.False(asked.IsCompleted);

        await second.AnswerAsync(id, new ConsoleAnswer(Continue: true), Cancellation);

        Assert.Equal(new ConsoleAnswer(Continue: true), await asked.WaitAsync(TimeSpan.FromSeconds(20), Cancellation));
    }

    // Continued on the web: the asker no longer needs the answer, and the console closes the question.
    [Fact]
    public async Task WithdrawsTheQuestionOnceTheAskerNoLongerNeedsIt()
    {
        _console.Start(() => SessionMachineConsole.CreatePipe(_pipe, CurrentUser));
        await using ConsoleClient client = await ConnectAsync();
        using CancellationTokenSource asking = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        Task<ConsoleAnswer?> asked = _console.AskAsync(s_pause, asking.Token);
        (QuestionMessage question, _) = await UntilAsync<QuestionMessage>(client);

        await asking.CancelAsync();

        Assert.Null(await asked);
        Assert.Equal(new WithdrawMessage(question.Id), (await UntilAsync<WithdrawMessage>(client)).Message);
    }

    // Enter pressed on the screen of a question withdrawn meanwhile, or an answer to an id never asked, answers nothing.
    [Fact]
    public async Task LeavesAnAnswerToAQuestionNoLongerOpenAlone()
    {
        _console.Start(() => SessionMachineConsole.CreatePipe(_pipe, CurrentUser));
        await using ConsoleClient client = await ConnectAsync();
        using CancellationTokenSource asking = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        Task<ConsoleAnswer?> withdrawn = _console.AskAsync(s_pause, asking.Token);
        (QuestionMessage old, _) = await UntilAsync<QuestionMessage>(client);
        await asking.CancelAsync();
        Assert.Null(await withdrawn);

        Task<ConsoleAnswer?> asked = _console.AskAsync(new PauseQuestion("Check the dock", "Plug the dock in."), Cancellation);
        (QuestionMessage current, _) = await UntilAsync<QuestionMessage>(client);

        await client.AnswerAsync(old.Id, new ConsoleAnswer(Back: true), Cancellation);
        await client.AnswerAsync(current.Id + 100, new ConsoleAnswer(Back: true), Cancellation);
        await client.AnswerAsync(current.Id, new ConsoleAnswer(Continue: true), Cancellation);

        Assert.NotEqual(old.Id, current.Id);
        Assert.Equal(new ConsoleAnswer(Continue: true), await asked.WaitAsync(TimeSpan.FromSeconds(20), Cancellation));
    }

    // An account asked for the run goes to the agent with its password, and no line of the log, on the console or in the
    // file, has the password in it.
    [Fact]
    public async Task NeverLogsWhatAnAnswerCarries()
    {
        _console.Start(() => SessionMachineConsole.CreatePipe(_pipe, CurrentUser));
        await using ConsoleClient client = await ConnectAsync();
        ConsoleInput account = new("Join", "Join account", null, ConsoleInputKind.Account, [], null, true, null, null, "corp.example");
        Task<ConsoleAnswer?> asked = _console.AskAsync(new InputsQuestion("Install Windows", [account], null), Cancellation);
        (QuestionMessage question, _) = await UntilAsync<QuestionMessage>(client);

        ConsoleInputValue typed = new("Join", null, @"CORP\join", Password);
        await client.AnswerAsync(question.Id, new ConsoleAnswer(Values: [typed]), Cancellation);
        ConsoleAnswer? answer = await asked.WaitAsync(TimeSpan.FromSeconds(20), Cancellation);
        _log.Information("Answered.");
        (_, List<string> lines) = await ReadAsync(client, "Answered.", withState: false);

        Assert.Equal([typed], answer?.Values);
        Assert.DoesNotContain(Password, _writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(lines, line => line.Contains(Password, StringComparison.Ordinal));
    }

    private Task<ConsoleClient> ConnectAsync(HelloMessage? hello = null) => ConsoleClient.ConnectAsync(
        new NamedPipeClientStream(".", _pipe, PipeDirection.InOut, PipeOptions.Asynchronous),
        hello ?? new HelloMessage(HelloMessage.CurrentVersion, "Test console"),
        null,
        TimeSpan.FromSeconds(10),
        Cancellation);

    // Reads until a message of that kind comes, and returns it with every message before it.
    private static async Task<(T Message, List<ConsoleMessage> Before)> UntilAsync<T>(ConsoleClient client)
        where T : ConsoleMessage
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        List<ConsoleMessage> before = [];

        while (true)
        {
            switch (await client.ReceiveAsync(timeout.Token))
            {
                case T wanted:
                    return (wanted, before);
                case null:
                    Assert.Fail($"The pipe closed before a {typeof(T).Name} came.");
                    break;
                case { } message:
                    before.Add(message);
                    break;
            }
        }
    }

    // Reads until the line comes, and returns the last state and every line read.
    private static async Task<(ConsoleState State, List<string> Lines)> ReadAsync(ConsoleClient client, string until, bool withState = true)
    {
        ConsoleState? state = null;
        List<string> lines = [];

        while (!lines.Contains(until))
        {
            switch (await client.ReceiveAsync(Cancellation))
            {
                case StateMessage message:
                    state = message.State;
                    break;
                case LogMessage log:
                    lines.AddRange(log.Lines.Select(line => line.Text));
                    break;
                case null:
                    Assert.Fail($"The pipe closed before \"{until}\" came.");
                    break;
            }
        }

        if (withState)
        {
            Assert.NotNull(state);
        }

        return (state!, lines);
    }
}
