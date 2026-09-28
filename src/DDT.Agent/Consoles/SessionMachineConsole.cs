// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// The console in the installed Windows: ddt-console.exe as the shell of DDT's session, which Windows starts at its
// auto-logon, not the agent. The agent keeps one pipe open for it for as long as it runs, and the console connects
// whenever it starts, again after a restart of either, and gets the whole state, the newest log lines and the open
// question each time. A question waits for a console while none is connected, as a Pause step's does while Windows
// restarts the session; the web can answer it meanwhile, and the asker then withdraws it. Nothing falls back: the service
// has no text console, and the log goes to the server and to its file all the same.
public sealed class SessionMachineConsole : IMachineConsole, IAsyncDisposable
{
    private const int BacklogLines = 500;
    private const int MaxUnsentLines = 5000;

    private static readonly TimeSpan s_closeTimeout = TimeSpan.FromSeconds(5);

    private readonly AgentLog _log;
    private readonly string _agentVersion;
    private readonly Action? _connected;
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Queue<ConsoleLogLine> _recent = new();
    private readonly Queue<ConsoleLogLine> _unsent = new();
    private readonly Queue<ConsoleMessage> _control = new();
    private readonly QuestionSlot _questions;
    private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ConsoleState? _state;
    private bool _stateUnsent;
    private bool _isConnected;
    private bool _closing;
    private bool _started;
    private Task _running = Task.CompletedTask;

    // connected is called each time a console has said hello.
    public SessionMachineConsole(AgentLog log, string agentVersion, Action? connected = null)
    {
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
        _agentVersion = agentVersion;
        _connected = connected;
        _questions = new QuestionSlot(Send);
    }

    // Someone can answer while a console is connected. A question asked meanwhile waits for the next one all the same.
    public bool CanAsk => IsConnected;

    public bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _isConnected;
            }
        }
    }

    // The pipe of DDT's session: only the agent's own account, SYSTEM, and the session's account may open it, and the
    // agent's account owns it, which the console checks. One instance, which the agent keeps and disconnects between
    // consoles, so its name never comes free for another process to take.
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

    // Opens the pipe with createPipe and serves consoles on it, in the background, until disposed. Until then the
    // console only keeps the state and the newest lines for the first console that connects.
    public void Start(Func<NamedPipeServerStream> createPipe)
    {
        ArgumentNullException.ThrowIfNull(createPipe);

        lock (_lock)
        {
            if (_started || _closing)
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

        lock (_lock)
        {
            _state = state;

            if (_isConnected)
            {
                _stateUnsent = true;
                _wake.TrySetResult();
            }
        }
    }

    public void Write(ConsoleLogLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        lock (_lock)
        {
            _recent.Enqueue(line);

            while (_recent.Count > BacklogLines)
            {
                _recent.Dequeue();
            }

            if (_isConnected)
            {
                _unsent.Enqueue(line);

                while (_unsent.Count > MaxUnsentLines)
                {
                    _unsent.Dequeue();
                }

                _wake.TrySetResult();
            }
        }
    }

    // Completes with the answer, or with null once cancelled, which withdraws the question, or once the agent ends.
    public async Task<ConsoleAnswer?> AskAsync(ConsoleQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        return (await _questions.AskAsync(question, cancellationToken).ConfigureAwait(false)).Answer;
    }

    // The last state and lines go out to a console that is connected, then the pipe closes.
    public async ValueTask DisposeAsync()
    {
        _questions.Close();

        bool connected;

        lock (_lock)
        {
            if (_closing)
            {
                return;
            }

            _closing = true;
            connected = _isConnected;
            _wake.TrySetResult();
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
            while (!stop.IsCancellationRequested && !Closing())
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
                    _log.Information($"The console of DDT's session closed its pipe ({exception.Message}). It can connect again.");
                }
                finally
                {
                    Disconnected();

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
            if (await GreetAsync(channel, stop).ConfigureAwait(false) is { } refused)
            {
                return refused;
            }
        }
        catch (ConsoleProtocolException exception)
        {
            return $"sent something that is not a console message ({exception.Message})";
        }

        lock (_lock)
        {
            _isConnected = true;
            _stateUnsent = _state is not null;

            foreach (ConsoleLogLine line in _recent)
            {
                _unsent.Enqueue(line);
            }

            _wake.TrySetResult();
        }

        // After the state and the lines, as the console shows the question over them.
        _questions.Connected();
        _connected?.Invoke();

        using CancellationTokenSource serving = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Task<string?> reading = ReadAsync(channel, serving.Token);
        Task<string?> writing = WriteAsync(channel, serving.Token);

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

    // Null once both sides said hello, otherwise why the console is refused.
    private async Task<string?> GreetAsync(ConsoleChannel channel, CancellationToken stop)
    {
        HelloMessage hello;

        using (CancellationTokenSource greeting = CancellationTokenSource.CreateLinkedTokenSource(stop))
        {
            greeting.CancelAfter(PipeMachineConsole.HelloTimeout);

            try
            {
                if (await channel.ReceiveAsync(greeting.Token).ConfigureAwait(false) is not HelloMessage received)
                {
                    return "did not begin with a hello";
                }

                hello = received;
            }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested)
            {
                return $"did not say hello within {Seconds(PipeMachineConsole.HelloTimeout)}";
            }
        }

        if (hello.Version != HelloMessage.CurrentVersion)
        {
            RefusedMessage refusal = new(
                HelloMessage.CurrentVersion,
                $"This agent speaks version {HelloMessage.CurrentVersion} of the console protocol, not {hello.Version}.");
            await SendAsync(channel, refusal, stop).ConfigureAwait(false);

            return $"speaks version {hello.Version} of the console protocol, and this agent version {HelloMessage.CurrentVersion}";
        }

        if (await SendAsync(channel, new HelloMessage(HelloMessage.CurrentVersion, $"DDT agent {_agentVersion}"), stop).ConfigureAwait(false) is { } unsent)
        {
            return unsent;
        }

        _log.Information($"The console of DDT's session, {hello.Program}, is connected.");

        return null;
    }

    // Answers until the pipe closes. An answer to a question no longer open, withdrawn or asked by an agent before a
    // restart, is left alone. Returns why it stopped.
    private async Task<string?> ReadAsync(ConsoleChannel channel, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                switch (await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false))
                {
                    case null:
                        return "closed its pipe";
                    case AnswerMessage answer:
                        _questions.Answer(answer.Id, answer.Answer);
                        break;
                    default:
                        return "sent a message only the agent sends";
                }
            }
        }
        catch (ConsoleProtocolException exception)
        {
            return $"sent something that is not a console message ({exception.Message})";
        }
        catch (IOException exception)
        {
            return $"closed its pipe ({exception.Message})";
        }
    }

    // Sends the newest state, the lines and the questions as they come. Returns null once the agent closes and everything
    // is sent, otherwise why it stopped.
    private async Task<string?> WriteAsync(ConsoleChannel channel, CancellationToken cancellationToken)
    {
        while (true)
        {
            List<ConsoleMessage> messages = [];
            Task wake;

            lock (_lock)
            {
                if (_stateUnsent)
                {
                    messages.Add(new StateMessage(_state!));
                    _stateUnsent = false;
                }

                while (_unsent.Count > 0)
                {
                    int count = Math.Min(_unsent.Count, AgentLimits.MaxLinesPerBatch);
                    ConsoleLogLine[] batch = new ConsoleLogLine[count];

                    for (int index = 0; index < count; index++)
                    {
                        batch[index] = _unsent.Dequeue();
                    }

                    messages.Add(new LogMessage(batch));
                }

                while (_control.TryDequeue(out ConsoleMessage? message))
                {
                    messages.Add(message);
                }

                if (messages.Count == 0)
                {
                    if (_closing)
                    {
                        return null;
                    }

                    _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                wake = _wake.Task;
            }

            if (messages.Count == 0)
            {
                await wake.WaitAsync(cancellationToken).ConfigureAwait(false);

                continue;
            }

            foreach (ConsoleMessage message in messages)
            {
                if (await SendAsync(channel, message, cancellationToken).ConfigureAwait(false) is { } problem)
                {
                    return problem;
                }
            }
        }
    }

    // Null once sent, otherwise why not.
    private static async Task<string?> SendAsync(ConsoleChannel channel, ConsoleMessage message, CancellationToken cancellationToken)
    {
        using CancellationTokenSource sending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sending.CancelAfter(PipeMachineConsole.SendTimeout);

        try
        {
            await channel.SendAsync(message, sending.Token).ConfigureAwait(false);

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"took no message for {Seconds(PipeMachineConsole.SendTimeout)}";
        }
        catch (IOException exception)
        {
            return $"closed its pipe ({exception.Message})";
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

    private void Disconnected()
    {
        lock (_lock)
        {
            _isConnected = false;
            _stateUnsent = false;
            _unsent.Clear();
            _control.Clear();
        }

        _questions.Disconnected();
    }

    // A question or a withdrawal from the slot, for the console that is connected.
    private void Send(ConsoleMessage message)
    {
        lock (_lock)
        {
            if (_isConnected)
            {
                _control.Enqueue(message);
                _wake.TrySetResult();
            }
        }
    }

    private bool Closing()
    {
        lock (_lock)
        {
            return _closing;
        }
    }

    private static string Seconds(TimeSpan timeout) => string.Create(CultureInfo.InvariantCulture, $"{timeout.TotalSeconds:0.#} s");
}
