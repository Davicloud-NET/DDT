// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;
using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// The console in the installed system: in Windows the shell of DDT's session, which Windows starts at the auto-logon, in
// Linux a unit on a terminal of its own. It connects whenever it starts. There's no fallback, because the service has no
// text console.
public sealed class SessionMachineConsole : IMachineConsole, IAsyncDisposable
{
    private static readonly TimeSpan s_closeTimeout = TimeSpan.FromSeconds(5);

    private readonly AgentLog _log;
    private readonly string _agentVersion;
    private readonly Action? _connected;
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ConsoleOutbox _outbox = new();
    private readonly QuestionSlot _questions;
    private bool _started;
    private Task _running = Task.CompletedTask;

    // connected is called each time a console has said hello.
    public SessionMachineConsole(AgentLog log, string agentVersion, Action? connected = null)
    {
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
        _agentVersion = agentVersion;
        _connected = connected;
        _questions = new QuestionSlot(_outbox.Send);
    }

    // A question asked while no console is connected still waits for the next console.
    public bool CanAsk => IsConnected;

    // The system may start the console at any moment.
    public bool AnswersLater => true;

    public bool IsConnected => _outbox.IsConnected;

    // How the log names it, starting in lower case.
    public string Name { get; init; } = "the console of DDT's session";

    private string Sentence => char.ToUpperInvariant(Name[0]) + Name[1..];

    // Serves consoles in the background on the pipe that createPipe opens, until disposed. The state and the newest
    // lines wait for the first console to connect. refuse says why a connected client is not the console, or null.
    public void Start(Func<NamedPipeServerStream> createPipe, Func<NamedPipeServerStream, string?>? refuse = null)
    {
        ArgumentNullException.ThrowIfNull(createPipe);

        lock (_lock)
        {
            if (_started || _outbox.IsClosing)
            {
                return;
            }

            _started = true;
        }

        _running = RunAsync(createPipe, refuse, _stop.Token);
    }

    public void Show(ConsoleState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _outbox.TryShow(state);
    }

    public void Write(ConsoleLogLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        _outbox.TryWrite(line);
    }

    // Null once cancelled, which withdraws the question, or once the agent ends. The web may answer meanwhile.
    public async Task<ConsoleAnswer?> AskAsync(ConsoleQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        return (await _questions.AskAsync(question, cancellationToken).ConfigureAwait(false)).Answer;
    }

    // The last state and lines go out to a console that is connected, then the pipe closes.
    public async ValueTask DisposeAsync()
    {
        _questions.Close();

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

    private async Task RunAsync(Func<NamedPipeServerStream> createPipe, Func<NamedPipeServerStream, string?>? refuse, CancellationToken stop)
    {
        await Task.Yield();

        NamedPipeServerStream pipe;

        try
        {
            pipe = createPipe();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.Warning($"The pipe for {Name} could not be opened ({exception.Message}). The run goes on without it.");

            return;
        }

        await using (pipe.ConfigureAwait(false))
        {
            while (!stop.IsCancellationRequested && !_outbox.IsClosing)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);

                    if (refuse?.Invoke(pipe) is { } refusal)
                    {
                        _log.Warning($"{refusal} It was let go.");
                    }
                    else if (await ServeAsync(pipe, stop).ConfigureAwait(false) is { } reason)
                    {
                        _log.Information($"{Sentence} {reason}. It can connect again.");
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException exception)
                {
                    _log.Information($"{Sentence} {ConsoleFailure.ClosedPipe(exception)}. It can connect again.");
                }
                finally
                {
                    _outbox.Disconnect();
                    _questions.Disconnected();

                    if (pipe.IsConnected)
                    {
                        await DrainAsync(pipe).ConfigureAwait(false);
                        pipe.Disconnect();
                    }
                }
            }
        }
    }

    // Why the console went away, or null when the agent closed the pipe.
    private async Task<string?> ServeAsync(NamedPipeServerStream pipe, CancellationToken stop)
    {
        using ConsoleChannel channel = new(pipe);

        try
        {
            (string? program, string? refusal, _) = await ConsoleHandshake.GreetAsync(channel, _agentVersion, stop).ConfigureAwait(false);

            if (refusal is not null)
            {
                return refusal;
            }

            _log.Information($"{Sentence}, {program}, is connected.");
        }
        catch (ConsoleProtocolException exception)
        {
            return ConsoleFailure.NotAMessage(exception);
        }

        // After the state and the lines, because the console shows the question on top of them.
        _outbox.Connect();
        _questions.Connected();
        _connected?.Invoke();

        using CancellationTokenSource serving = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Task<string?> reading = ConsoleAnswers.ReadAsync(channel, _questions, serving.Token);
        Task<string?> writing = ConsoleSender.SendQueuedAsync(channel, _outbox, serving.Token);

        try
        {
            Task<string?> first = await Task.WhenAny(reading, writing).ConfigureAwait(false);

            return await first.ConfigureAwait(false);
        }
        finally
        {
            await serving.CancelAsync().ConfigureAwait(false);
            await ((Task)reading).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await ((Task)writing).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    // A disconnect throws away what the console hasn't read yet, such as the last state when the agent ends. So the
    // console gets a moment to read it first. A console that stopped reading doesn't hold the agent up. A socket on
    // Linux keeps what it delivered.
    private static async Task DrainAsync(NamedPipeServerStream pipe)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            await Task.Run(pipe.WaitForPipeDrain).WaitAsync(s_closeTimeout).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or ObjectDisposedException)
        {
        }
    }
}
