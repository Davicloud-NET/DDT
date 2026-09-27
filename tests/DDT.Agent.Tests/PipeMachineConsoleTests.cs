// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.IO.Pipes;
using DDT.Agent.Consoles;
using DDT.ConsoleProtocol;
using Xunit;

namespace DDT.Agent.Tests;

// The graphical console over a real named pipe, with a console in this process at the other end, and the text console
// behind it, which takes over whenever the graphical one fails.
public sealed class PipeMachineConsoleTests : IAsyncDisposable
{
    private static readonly SignInQuestion s_userName = new(SignInField.UserName, null, null);

    private readonly StringWriter _console = new();
    private readonly List<PipeMachineConsole> _created = [];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ddt-console-tests-{Guid.NewGuid():N}");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        foreach (PipeMachineConsole console in _created)
        {
            await console.DisposeAsync();
        }

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task SendsTheStateTheLinesAndTheQuestionAndTakesTheAnswer()
    {
        FakeGraphicalConsole graphical = new(question => question is SignInQuestion ? new ConsoleAnswer(Text: "bob") : null);
        (PipeMachineConsole console, AgentLog log, _) = Create(new FakeConsoleLauncher(graphical.RunAsync));
        log.Information("Written before the console connected.");
        ConsoleStatus status = TestAgents.Status(console);

        console.Start();
        ConsoleAnswer? answer = await console.AskAsync(s_userName, Cancellation);

        Assert.Equal(new ConsoleAnswer(Text: "bob"), answer);
        Assert.False(console.FellBack);

        // What changes after the console connected reaches it too, in order.
        status.Registering();
        log.Warning("Written after.");
        await UntilAsync(() => Lines(graphical).Contains("Written after."));

        List<ConsoleMessage> received = graphical.Received;
        Assert.Equal(ConsoleStage.Starting, Assert.IsType<StateMessage>(received[0]).State.Stage);
        Assert.Equal(
            ["Written before the console connected.", "The graphical console, Fake console, is connected.", "Written after."],
            Lines(graphical));
        Assert.Contains(received, message => message is QuestionMessage { Question: SignInQuestion { Field: SignInField.UserName } });
        Assert.Equal(ConsoleStage.Connecting, received.OfType<StateMessage>().Last().State.Stage);
        Assert.True(
            received.FindIndex(message => message is StateMessage { State.Stage: ConsoleStage.Connecting })
            < received.FindLastIndex(message => message is LogMessage));
    }

    [Fact]
    public async Task WithdrawsAQuestionTheAgentNoLongerNeeds()
    {
        FakeGraphicalConsole graphical = new();
        (PipeMachineConsole console, _, _) = Create(new FakeConsoleLauncher(graphical.RunAsync));
        console.Start();

        using CancellationTokenSource asking = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        Task<ConsoleAnswer?> asked = console.AskAsync(s_userName, asking.Token);
        await UntilAsync(() => graphical.Received.OfType<QuestionMessage>().Any());
        await asking.CancelAsync();

        Assert.Null(await asked);
        await UntilAsync(() => graphical.Received.OfType<WithdrawMessage>().Any());
        Assert.Equal(graphical.Received.OfType<QuestionMessage>().Single().Id, graphical.Received.OfType<WithdrawMessage>().Single().Id);
        Assert.False(console.FellBack);
    }

    [Fact]
    public async Task AsksTheOpenQuestionOnTheTextConsoleWhenTheConsoleCrashes()
    {
        FakeGraphicalConsole graphical = new() { CrashAt = message => message is QuestionMessage };
        ScriptedSignInPrompt prompt = new("bob", "secret");
        FakeConsoleLauncher launcher = new(graphical.RunAsync);
        (PipeMachineConsole console, _, _) = Create(launcher, prompt);
        console.Start();

        ConsoleAnswer? answer = await console.AskAsync(s_userName, Cancellation);

        Assert.Equal(new ConsoleAnswer(Text: "bob"), answer);
        Assert.Equal(["User name"], prompt.Labels);
        Assert.True(console.FellBack);
        Assert.Contains(
            ConsoleLines(),
            line => line.EndsWith("WARN  The graphical console ended (exit code 0xE0434352). The agent carries on with the text console.", StringComparison.Ordinal));
        Assert.True(launcher.Started!.Stopped);

        // The rest of the run stays with the text console.
        Assert.Equal(new ConsoleAnswer(Text: "secret"), await console.AskAsync(new SignInQuestion(SignInField.Password, "bob", null), Cancellation));
        Assert.Equal(["User name", "Password for bob (hidden)"], prompt.Labels);
        Assert.Single(graphical.Received.OfType<QuestionMessage>());
    }

    [Fact]
    public async Task RefusesAConsoleOfAnotherVersionAndCarriesOnWithTheTextConsole()
    {
        FakeGraphicalConsole graphical = new() { Hello = new HelloMessage(HelloMessage.CurrentVersion + 1, "Future console") };
        ScriptedSignInPrompt prompt = new("bob");
        FakeConsoleLauncher launcher = new(graphical.RunAsync);
        (PipeMachineConsole console, _, _) = Create(launcher, prompt);
        console.Start();

        Assert.Equal(new ConsoleAnswer(Text: "bob"), await console.AskAsync(s_userName, Cancellation));

        // It heard why, and ended by itself.
        Assert.Equal(2, await launcher.Started!.Exited);
        Assert.Equal(
            $"This agent speaks version {HelloMessage.CurrentVersion} of the console protocol, not {HelloMessage.CurrentVersion + 1}.",
            graphical.Refusal);
        Assert.Contains(
            ConsoleLines(),
            line => line.EndsWith(
                $"The graphical console speaks version {HelloMessage.CurrentVersion + 1} of the console protocol, and this agent version " +
                $"{HelloMessage.CurrentVersion}. The agent carries on with the text console.",
                StringComparison.Ordinal));
        Assert.Empty(graphical.Received);
    }

    [Fact]
    public async Task FallsBackWhenTheConsoleEndsBeforeItConnects()
    {
        (PipeMachineConsole console, _, _) = Create(new FakeConsoleLauncher((_, _) => Task.FromResult(3)), new ScriptedSignInPrompt("bob"));
        console.Start();

        Assert.Equal(new ConsoleAnswer(Text: "bob"), await console.AskAsync(s_userName, Cancellation));
        Assert.Contains(ConsoleLines(), line => line.EndsWith("The graphical console ended before it connected (exit code 0x00000003). The agent carries on with the text console.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivesUpOnAConsoleThatDoesNotConnectInTime()
    {
        FakeConsoleLauncher launcher = new(async (_, killed) =>
        {
            await Task.Delay(Timeout.Infinite, killed);

            return 0;
        });
        (PipeMachineConsole console, _, _) = Create(launcher, new ScriptedSignInPrompt("bob"), TimeSpan.FromMilliseconds(200));
        console.Start();

        Assert.Equal(new ConsoleAnswer(Text: "bob"), await console.AskAsync(s_userName, Cancellation));
        Assert.Contains(ConsoleLines(), line => line.EndsWith("The graphical console did not connect within 0.2 s. The agent carries on with the text console.", StringComparison.Ordinal));
        Assert.Equal(-1, await launcher.Started!.Exited);
    }

    [Fact]
    public async Task FallsBackWhenTheConsoleCannotBeStarted()
    {
        ThrowingLauncher launcher = new();
        (PipeMachineConsole console, _, _) = Create(launcher, new ScriptedSignInPrompt("bob"));
        console.Start();

        Assert.Equal(new ConsoleAnswer(Text: "bob"), await console.AskAsync(s_userName, Cancellation));
        Assert.Contains(ConsoleLines(), line => line.EndsWith("The graphical console could not be started (The system cannot find the file specified.). The agent carries on with the text console.", StringComparison.Ordinal));
    }

    // The process launcher, with a console that exits before it connects and one that does not exist.
    [Fact]
    public async Task StartsTheConsoleAsAProcessWithThePipesName()
    {
        Directory.CreateDirectory(_directory);
        string arguments = Path.Combine(_directory, "arguments.txt");
        string console = Path.Combine(_directory, "console.cmd");
        await File.WriteAllTextAsync(console, $"@echo %* > \"{arguments}\"\r\n@exit /b 3\r\n", Cancellation);

        (PipeMachineConsole started, _, _) = Create(new ProcessConsoleLauncher(console), new ScriptedSignInPrompt("bob"));
        started.Start();

        Assert.Equal(new ConsoleAnswer(Text: "bob"), await started.AskAsync(s_userName, Cancellation));
        Assert.Matches("^--pipe ddt-console-[0-9a-f]{32} $", (await File.ReadAllTextAsync(arguments, Cancellation)).TrimEnd('\r', '\n'));
        Assert.Contains(ConsoleLines(), line => line.EndsWith("The graphical console ended before it connected (exit code 0x00000003). The agent carries on with the text console.", StringComparison.Ordinal));

        (PipeMachineConsole missing, _, _) = Create(new ProcessConsoleLauncher(Path.Combine(_directory, "missing.exe")), new ScriptedSignInPrompt("bob"));
        missing.Start();

        Assert.Equal(new ConsoleAnswer(Text: "bob"), await missing.AskAsync(s_userName, Cancellation));
        Assert.Contains(ConsoleLines(), line => line.Contains("The graphical console could not be started (", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EndsAConsoleThatSendsWhatOnlyTheAgentSends()
    {
        FakeConsoleLauncher launcher = new(async (pipeName, killed) =>
        {
            await using NamedPipeClientStream pipe = ConsolePipe.CreateClient(pipeName);
            await pipe.ConnectAsync(TimeSpan.FromSeconds(10), killed);
            using ConsoleChannel channel = new(pipe);
            await channel.SendAsync(new HelloMessage(HelloMessage.CurrentVersion, "Confused console"), killed);
            await channel.ReceiveAsync(killed);

            // A console only answers; this one withdraws, and then waits to be ended.
            await channel.SendAsync(new WithdrawMessage(1), killed);
            await Task.Delay(Timeout.Infinite, killed);

            return 0;
        });
        (PipeMachineConsole console, _, _) = Create(launcher, new ScriptedSignInPrompt("bob"));
        console.Start();

        Assert.Equal(new ConsoleAnswer(Text: "bob"), await console.AskAsync(s_userName, Cancellation));
        Assert.Contains(ConsoleLines(), line => line.EndsWith("The graphical console sent a message only the agent sends. The agent carries on with the text console.", StringComparison.Ordinal));
        Assert.Equal(-1, await launcher.Started!.Exited);
    }

    [Fact]
    public async Task TheLastStateReachesTheConsoleWhenTheAgentEnds()
    {
        FakeGraphicalConsole graphical = new();
        FakeConsoleLauncher launcher = new(graphical.RunAsync);
        (PipeMachineConsole console, _, _) = Create(launcher);
        ConsoleStatus status = TestAgents.Status(console);
        console.Start();
        await UntilAsync(() => graphical.Received.OfType<StateMessage>().Any());

        status.Stopped("The agent was stopped.");
        await console.DisposeAsync();

        // The console saw the pipe close and ended by itself; it was not ended.
        Assert.Equal(0, await launcher.Started!.Exited);
        Assert.False(launcher.Started.Stopped);
        Assert.Equal(ConsoleStage.Stopped, graphical.Received.OfType<StateMessage>().Last().State.Stage);
        Assert.DoesNotContain(ConsoleLines(), line => line.Contains("carries on with the text console", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ClosingForANewerAgentEndsTheConsoleWithoutAWord()
    {
        FakeGraphicalConsole graphical = new();
        FakeConsoleLauncher launcher = new(graphical.RunAsync);
        (PipeMachineConsole console, _, _) = Create(launcher);
        console.Start();
        await UntilAsync(() => graphical.Received.OfType<LogMessage>().Any());

        console.Close();

        Assert.True(launcher.Started!.Stopped);
        Assert.True(console.FellBack);
        Assert.DoesNotContain(ConsoleLines(), line => line.Contains("carries on with the text console", StringComparison.Ordinal));
    }

    [Fact]
    public void StartsTheConsoleNextToTheAgentOnlyWhereSomeoneMayBeAtTheMachine()
    {
        Directory.CreateDirectory(_directory);
        string besideAgent = Path.Combine(_directory, ConsolePipe.FileName);
        AgentOptions options = new(new Uri("https://ddt.example:8443/"), null, DryRun: false, 1, NoUpdate: false, null);

        Assert.Null(PipeMachineConsole.PathFor(options, inputRedirected: false, _directory));

        File.WriteAllBytes(besideAgent, []);

        Assert.Equal(besideAgent, PipeMachineConsole.PathFor(options, inputRedirected: false, _directory));
        Assert.Null(PipeMachineConsole.PathFor(options, inputRedirected: true, _directory));
        Assert.Null(PipeMachineConsole.PathFor(options with { DryRun = true }, inputRedirected: false, _directory));

        // Asked for by name, it starts whatever the case.
        AgentOptions named = options with { DryRun = true, ConsolePath = @"C:\Tools\console.exe" };
        Assert.Equal(@"C:\Tools\console.exe", PipeMachineConsole.PathFor(named, inputRedirected: true, _directory));
    }

    private (PipeMachineConsole Console, AgentLog Log, TextMachineConsole Text) Create(
        IConsoleLauncher launcher,
        ScriptedSignInPrompt? prompt = null,
        TimeSpan? connectTimeout = null)
    {
        AgentLog log = new(new ImmediateTimeProvider(), _console);
        TextMachineConsole text = new(prompt ?? new ScriptedSignInPrompt(), log);
        PipeMachineConsole console = new(text, launcher, log, TestAgents.Version, connectTimeout);
        log.MachineConsole = console;
        _created.Add(console);

        return (console, log, text);
    }

    private string[] ConsoleLines() => _console.ToString().Split(Environment.NewLine);

    private static List<string> Lines(FakeGraphicalConsole graphical) =>
        [.. graphical.Received.OfType<LogMessage>().SelectMany(message => message.Lines).Select(line => line.Text)];

    private static async Task UntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class ThrowingLauncher : IConsoleLauncher
    {
        public IConsoleProcess Start(string pipeName) => throw new Win32Exception(2, "The system cannot find the file specified.");
    }
}
