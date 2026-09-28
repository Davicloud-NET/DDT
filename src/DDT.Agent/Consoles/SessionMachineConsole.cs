// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// The console in the installed Windows: ddt-console.exe as the shell of DDT's session, which Windows starts at its
// auto-logon and which connects whenever it starts. Nothing falls back: the service has no text console.
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

    // A question asked while no console is connected waits for the next one all the same.
    public bool CanAsk => IsConnected;

    public bool IsConnected => _outbox.IsConnected;

    // Only the agent's account, SYSTEM, and the session's account may open it, and the agent's account owns it, which the
    // console checks. The agent keeps its one instance between consoles, so the name never comes free for another process.
    [SupportedOSPlatform("windows")]
    public static NamedPipeServerStream CreatePipe(string name, SecurityIdentifier console)
    {
        ArgumentNullException.ThrowIfNull(console);

        SecurityIdentifier agent = WindowsIdentity.GetCurrent().User!;
        PipeSecurity security = new();
        security.SetOwner(agent);
        security.AddAccessRule(new PipeAccessRule(agent, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(console, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            name,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            0,
            0,
            security);
    }

    // Serves consoles on the pipe createPipe opens, in the background, until disposed. Until then the state and the
    // newest lines wait for the first console.
    public void Start(Func<NamedPipeServerStream> createPipe)
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

        _running = RunAsync(createPipe, _stop.Token);
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

    private async Task RunAsync(Func<NamedPipeServerStream> createPipe, CancellationToken stop)
    {
        await Task.Yield();

        NamedPipeServerStream pipe;

        try
        {
            pipe = createPipe();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.Warning($"The pipe for the console of DDT's session could not be opened ({exception.Message}). The run goes on without it.");

            return;
        }

        await using (pipe.ConfigureAwait(false))
        {
            while (!stop.IsCancellationRequested && !_outbox.IsClosing)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);

                    if (await ServeAsync(pipe, stop).ConfigureAwait(false) is { } reason)
                    {
                        _log.Information($"The console of DDT's session {reason}. It can connect again.");
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException exception)
                {
                    _log.Information($"The console of DDT's session {ConsoleFailure.ClosedPipe(exception)}. It can connect again.");
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

            _log.Information($"The console of DDT's session, {program}, is connected.");
        }
        catch (ConsoleProtocolException exception)
        {
            return ConsoleFailure.NotAMessage(exception);
        }

        // After the state and the lines, as the console shows the question over them.
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

    // A disconnect throws away what the console has not read yet, such as the last state as the agent ends, so the
    // console gets a moment to read it first. One that reads nothing any more does not hold the agent up.
    private static async Task DrainAsync(NamedPipeServerStream pipe)
    {
        try
        {
            await Task.Run(pipe.WaitForPipeDrain).WaitAsync(s_closeTimeout).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or ObjectDisposedException)
        {
        }
    }
}
