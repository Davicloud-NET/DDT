// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.IO.Pipes;
using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// ddt-console.exe, the graphical console, fed over a named pipe. A console that fails in any way is ended, so the text
// console behind it is seen, which takes over for the rest of the run and asks an open question again.
public sealed class PipeMachineConsole : IMachineConsole, IAsyncDisposable
{
    // How long the console has to start and connect.
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(30);

    // How long a console whose pipe closed has to end, so its exit code can say what happened.
    private static readonly TimeSpan s_exitGrace = TimeSpan.FromSeconds(1);

    // How long the last messages may take to go out when the agent ends or hands over.
    private static readonly TimeSpan s_closeTimeout = TimeSpan.FromSeconds(5);

    private readonly IMachineConsole _fallback;
    private readonly IConsoleLauncher _launcher;
    private readonly AgentLog _log;
    private readonly string _agentVersion;
    private readonly TimeSpan _connectTimeout;
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ConsoleOutbox _outbox = new();
    private readonly QuestionSlot _questions;
    private bool _endConsole;
    private bool _greeted;
    private Task _running = Task.CompletedTask;

    // fallback is the text console. connectTimeout is only shortened by tests.
    public PipeMachineConsole(
        IMachineConsole fallback,
        IConsoleLauncher launcher,
        AgentLog log,
        string agentVersion,
        TimeSpan? connectTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(log);

        _fallback = fallback;
        _launcher = launcher;
        _log = log;
        _agentVersion = agentVersion;
        _connectTimeout = connectTimeout ?? DefaultConnectTimeout;
        _questions = new QuestionSlot(_outbox.Send);
    }

    // The console may still be starting: a question waits for it, or for the text console should it not come.
    public bool CanAsk => !_outbox.IsShut || _fallback.CanAsk;

    // True once the text console has taken over.
    public bool FellBack => _outbox.IsShut;

    // True once the console has said hello in this agent's version of the protocol, so it can show a run of this agent
    // anywhere, the installed Windows included.
    public bool Greeted
    {
        get
        {
            lock (_lock)
            {
                return _greeted;
            }
        }
    }

    // ddt-console.exe beside the agent, unless a dry run or redirected input says nobody is at the machine. --console
    // names one to start whatever the case.
    public static string? PathFor(AgentOptions options, bool inputRedirected, string agentDirectory)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ConsolePath is { } path)
        {
            return path;
        }

        string besideAgent = Path.Combine(agentDirectory, ConsolePipe.FileName);

        return !options.DryRun && !inputRedirected && File.Exists(besideAgent) ? besideAgent : null;
    }

    // Opens the pipe and starts the console, in the background.
    public void Start() => _running = RunAsync(_stop.Token);

    public void Show(ConsoleState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!_outbox.TryShow(state))
        {
            _fallback.Show(state);
        }
    }

    public void Write(ConsoleLogLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (!_outbox.TryWrite(line))
        {
            _fallback.Write(line);
        }
    }

    // Should the console go away before it answers, the text console asks again.
    public async Task<ConsoleAnswer?> AskAsync(ConsoleQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        if (!FellBack)
        {
            QuestionOutcome outcome = await _questions.AskAsync(question, cancellationToken).ConfigureAwait(false);

            if (!outcome.Gone || cancellationToken.IsCancellationRequested)
            {
                return outcome.Answer;
            }
        }

        return _fallback.CanAsk ? await _fallback.AskAsync(question, cancellationToken).ConfigureAwait(false) : null;
    }

    // Ends the console at once, for an agent that switches to a newer one, which starts a console of its own.
    public void Close()
    {
        lock (_lock)
        {
            _endConsole = true;
        }

        _stop.Cancel();

        try
        {
            _running.Wait(s_closeTimeout);
        }
        catch (AggregateException)
        {
        }
    }

    // The last state and lines go out first, unless the console never connected. The console is left running to show
    // them.
    public async ValueTask DisposeAsync()
    {
        if (!_outbox.TryBeginClosing(out bool connected))
        {
            return;
        }

        if (connected)
        {
            await Task.WhenAny(_running, Task.Delay(s_closeTimeout)).ConfigureAwait(false);
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        await _running.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken stop)
    {
        // Off the caller's thread: starting a process takes a moment.
        await Task.Yield();

        string? reason;

        try
        {
            string pipeName = ConsolePipe.NewName();
            NamedPipeServerStream pipe = ConsolePipe.CreateServer(pipeName);

            await using (pipe.ConfigureAwait(false))
            {
                using IConsoleProcess process = _launcher.Start(pipeName);
                reason = await ServeAsync(pipe, process, stop).ConfigureAwait(false);

                bool end;

                lock (_lock)
                {
                    end = reason is not null || _endConsole;
                }

                if (end)
                {
                    process.Stop();
                }
            }
        }
        catch (Exception exception)
        {
            // Whatever it is, the text console is there.
            reason = $"could not be started ({exception.Message})";
        }

        FallBack(reason);
    }

    // Why the console went away, or null when the agent closed it.
    private async Task<string?> ServeAsync(NamedPipeServerStream pipe, IConsoleProcess process, CancellationToken stop)
    {
        try
        {
            if (await ConnectAsync(pipe, process, stop).ConfigureAwait(false) is { } notConnected)
            {
                return notConnected;
            }

            using ConsoleChannel channel = new(pipe);

            if (await GreetAsync(channel, process, stop).ConfigureAwait(false) is { } refused)
            {
                return refused;
            }

            // After the state and the lines, as the console shows the question over them.
            _outbox.Connect();
            _questions.Connected();

            return await ServeConnectedAsync(channel, process, stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return null;
        }
        catch (ConsoleProtocolException exception)
        {
            return ConsoleFailure.NotAMessage(exception);
        }
        catch (IOException exception)
        {
            return ConsoleFailure.ClosedPipe(exception);
        }
    }

    // Reads answers and sends what comes until one of them stops or the console ends. Both have stopped when it returns,
    // so the channel can go.
    private async Task<string?> ServeConnectedAsync(ConsoleChannel channel, IConsoleProcess process, CancellationToken stop)
    {
        using CancellationTokenSource serving = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Task<string?> reading = ConsoleAnswers.ReadAsync(channel, _questions, serving.Token);
        Task<string?> writing = ConsoleSender.SendQueuedAsync(channel, _outbox, serving.Token);

        try
        {
            Task first = await Task.WhenAny(reading, writing, process.Exited).ConfigureAwait(false);
            stop.ThrowIfCancellationRequested();

            string? reason = first == process.Exited
                ? $"ended ({ExitCode(process)})"
                : await (first == reading ? reading : writing).ConfigureAwait(false);

            // A console that crashed closes its pipe as it ends, and its exit code says more.
            if (reason is not null
                && first != process.Exited
                && await Task.WhenAny(process.Exited, Task.Delay(s_exitGrace, stop)).ConfigureAwait(false) == process.Exited)
            {
                reason = $"ended ({ExitCode(process)})";
            }

            return reason;
        }
        finally
        {
            await serving.CancelAsync().ConfigureAwait(false);
            await ((Task)reading).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await ((Task)writing).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    // Null once the console is connected, otherwise why not.
    private async Task<string?> ConnectAsync(NamedPipeServerStream pipe, IConsoleProcess process, CancellationToken stop)
    {
        using CancellationTokenSource connecting = CancellationTokenSource.CreateLinkedTokenSource(stop);
        connecting.CancelAfter(_connectTimeout);
        Task connection = pipe.WaitForConnectionAsync(connecting.Token);

        if (await Task.WhenAny(connection, process.Exited).ConfigureAwait(false) != connection)
        {
            await connecting.CancelAsync().ConfigureAwait(false);
            await connection.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            stop.ThrowIfCancellationRequested();

            return $"ended before it connected ({ExitCode(process)})";
        }

        try
        {
            await connection.ConfigureAwait(false);

            return null;
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            return $"did not connect within {Durations.Describe(_connectTimeout)}";
        }
    }

    // Null once both sides said hello, otherwise why the console is refused.
    private async Task<string?> GreetAsync(ConsoleChannel channel, IConsoleProcess process, CancellationToken stop)
    {
        (string? program, string? refusal, bool told) = await ConsoleHandshake.GreetAsync(channel, _agentVersion, stop).ConfigureAwait(false);

        if (refusal is not null)
        {
            // A console told why gets a moment to end by itself before it is ended.
            if (told)
            {
                await Task.WhenAny(process.Exited, Task.Delay(s_exitGrace, stop)).ConfigureAwait(false);
            }

            return refusal;
        }

        _log.Information($"The graphical console, {program}, is connected.");

        lock (_lock)
        {
            _greeted = true;
        }

        return null;
    }

    // For the rest of the run. reason is null when the agent closed the console itself.
    private void FallBack(string? reason)
    {
        if (!_outbox.TryShut(out ConsoleState? state))
        {
            return;
        }

        if (reason is not null)
        {
            _log.Warning($"The graphical console {reason}. The agent carries on with the text console.");
        }

        if (state is not null)
        {
            _fallback.Show(state);
        }

        // The open question is asked again there.
        _questions.Close();
    }

    private static string ExitCode(IConsoleProcess process) =>
        process.Exited.IsCompletedSuccessfully
            ? string.Create(CultureInfo.InvariantCulture, $"exit code 0x{process.Exited.Result:X8}")
            : "no exit code";
}
