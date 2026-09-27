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
        Assert.False(_console.CanAsk);
        Assert.Null(await _console.AskAsync(new SignInQuestion(SignInField.UserName, null, null), Cancellation));
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

    private Task<ConsoleClient> ConnectAsync(HelloMessage? hello = null) => ConsoleClient.ConnectAsync(
        new NamedPipeClientStream(".", _pipe, PipeDirection.InOut, PipeOptions.Asynchronous),
        hello ?? new HelloMessage(HelloMessage.CurrentVersion, "Test console"),
        null,
        TimeSpan.FromSeconds(10),
        Cancellation);

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
