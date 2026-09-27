// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.IO.Pipes;
using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// ddt-console.exe, the graphical console, fed over a named pipe. Once it has said hello in the agent's version of the
// protocol, it gets the whole state, the newest log lines from before it connected, and the open question, and then
// every change as it comes. Should it not start, not connect in time, speak another version, send what is not a console
// message, stop reading, close its pipe or end, the agent ends it, so the text console behind it is seen, and falls
// back to the text console for the rest of its run: an open question is asked there again, and nothing waits for a
// console that is gone. When the agent ends, the console is left running to show the last state.
public sealed class PipeMachineConsole : IMachineConsole, IAsyncDisposable
{
    // How long the console has to start and connect, and then to say hello.
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);

    // A console that takes no message for this long has stopped reading.
    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);

    // The lines kept for a console that connects late, and for one that reads slowly.
    private const int BacklogLines = 500;
    private const int MaxUnsentLines = 5000;

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
    private readonly Queue<ConsoleLogLine> _lines = new();
    private readonly Queue<ConsoleMessage> _control = new();
    private readonly Dictionary<int, PendingQuestion> _pending = [];
    private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ConsoleState? _state;
    private bool _stateUnsent;
    private bool _connected;
    private bool _fellBack;
    private bool _closing;
    private bool _endConsole;
    private bool _greeted;
    private int _lastQuestionId;
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
    }

    // The console may still be starting: a question waits for it, or for the text console should it not come.
    public bool CanAsk
    {
        get
        {
            lock (_lock)
            {
                if (!_fellBack)
                {
                    return true;
                }
            }

            return _fallback.CanAsk;
        }
    }

    // True once the text console has taken over.
    public bool FellBack
    {
        get
        {
            lock (_lock)
            {
                return _fellBack;
            }
        }
    }

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

    // The graphical console to start, or null for the text console alone. It is ddt-console.exe next to the agent, when
    // there is one and someone may be at the machine: never in a dry run, or with input redirected, as by a script.
    // --console names one to start whatever the case.
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

        lock (_lock)
        {
            if (!_fellBack)
            {
                _state = state;

                if (_connected)
                {
                    _stateUnsent = true;
                    _wake.TrySetResult();
                }

                return;
            }
        }

        _fallback.Show(state);
    }

    public void Write(ConsoleLogLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        lock (_lock)
        {
            if (!_fellBack)
            {
                _lines.Enqueue(line);

                while (_lines.Count > (_connected ? MaxUnsentLines : BacklogLines))
                {
                    _lines.Dequeue();
                }

                if (_connected)
                {
                    _wake.TrySetResult();
                }

                return;
            }
        }

        _fallback.Write(line);
    }

    public async Task<ConsoleAnswer?> AskAsync(ConsoleQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        PendingQuestion? pending = null;

        lock (_lock)
        {
            if (!_fellBack)
            {
                pending = new PendingQuestion(++_lastQuestionId, question);
                _pending.Add(pending.Id, pending);

                if (_connected)
                {
                    _control.Enqueue(new QuestionMessage(pending.Id, question));
                    _wake.TrySetResult();
                }
            }
        }

        if (pending is not null)
        {
            PendingAnswer answer;

            using (cancellationToken.Register(() => Withdraw(pending)))
            {
                answer = await pending.Answer.Task.ConfigureAwait(false);
            }

            if (!answer.FellBack || cancellationToken.IsCancellationRequested)
            {
                return answer.Answer;
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

    // The last state and lines go out first, unless the console never connected.
    public async ValueTask DisposeAsync()
    {
        bool connected;

        lock (_lock)
        {
            if (_closing)
            {
                return;
            }

            _closing = true;
            connected = _connected;
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

            Connected();

            return await ServeConnectedAsync(channel, process, stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return null;
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

    // Reads answers and sends what comes until one of them stops or the console ends. Both have stopped when it returns,
    // so the channel can go.
    private async Task<string?> ServeConnectedAsync(ConsoleChannel channel, IConsoleProcess process, CancellationToken stop)
    {
        using CancellationTokenSource serving = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Task<string?> reading = ReadAsync(channel, serving.Token);
        Task<string?> writing = WriteAsync(channel, serving.Token);

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
            return $"did not connect within {Seconds(_connectTimeout)}";
        }
    }

    // Null once both sides said hello, otherwise why the console is refused.
    private async Task<string?> GreetAsync(ConsoleChannel channel, IConsoleProcess process, CancellationToken stop)
    {
        HelloMessage hello;

        using (CancellationTokenSource greeting = CancellationTokenSource.CreateLinkedTokenSource(stop))
        {
            greeting.CancelAfter(HelloTimeout);

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
                return $"did not say hello within {Seconds(HelloTimeout)}";
            }
        }

        if (hello.Version != HelloMessage.CurrentVersion)
        {
            // Told why, and given a moment to end by itself before it is ended.
            RefusedMessage refusal = new(
                HelloMessage.CurrentVersion,
                $"This agent speaks version {HelloMessage.CurrentVersion} of the console protocol, not {hello.Version}.");

            if (await SendAsync(channel, refusal, stop).ConfigureAwait(false) is null)
            {
                await Task.WhenAny(process.Exited, Task.Delay(s_exitGrace, stop)).ConfigureAwait(false);
            }

            return $"speaks version {hello.Version} of the console protocol, and this agent version {HelloMessage.CurrentVersion}";
        }

        HelloMessage accepted = new(HelloMessage.CurrentVersion, $"DDT agent {_agentVersion}");

        if (await SendAsync(channel, accepted, stop).ConfigureAwait(false) is { } unsent)
        {
            return unsent;
        }

        _log.Information($"The graphical console, {hello.Program}, is connected.");

        lock (_lock)
        {
            _greeted = true;
        }

        return null;
    }

    // Answers until the pipe closes. Returns why it stopped.
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
                        Answered(answer);
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

    // Sends the newest state, the lines and the questions as they come. Returns null once the agent closes the console
    // and everything is sent, otherwise why it stopped.
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

                while (_lines.Count > 0)
                {
                    int count = Math.Min(_lines.Count, AgentLimits.MaxLinesPerBatch);
                    ConsoleLogLine[] batch = new ConsoleLogLine[count];

                    for (int index = 0; index < count; index++)
                    {
                        batch[index] = _lines.Dequeue();
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
        sending.CancelAfter(SendTimeout);

        try
        {
            await channel.SendAsync(message, sending.Token).ConfigureAwait(false);

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"took no message for {Seconds(SendTimeout)}";
        }
        catch (IOException exception)
        {
            return $"closed its pipe ({exception.Message})";
        }
    }

    // What waits for the console goes out now: the state, the lines kept, and the open question.
    private void Connected()
    {
        lock (_lock)
        {
            _connected = true;
            _stateUnsent = _state is not null;

            foreach (PendingQuestion pending in _pending.Values.OrderBy(pending => pending.Id))
            {
                _control.Enqueue(new QuestionMessage(pending.Id, pending.Question));
            }

            _wake.TrySetResult();
        }
    }

    private void Answered(AnswerMessage message)
    {
        PendingQuestion? pending;

        lock (_lock)
        {
            // Withdrawn meanwhile, or never asked.
            if (!_pending.Remove(message.Id, out pending))
            {
                return;
            }
        }

        pending.Answer.TrySetResult(new PendingAnswer(false, message.Answer));
    }

    private void Withdraw(PendingQuestion pending)
    {
        lock (_lock)
        {
            if (!_pending.Remove(pending.Id))
            {
                return;
            }

            if (_connected)
            {
                _control.Enqueue(new WithdrawMessage(pending.Id));
                _wake.TrySetResult();
            }
        }

        pending.Answer.TrySetResult(new PendingAnswer(false, null));
    }

    // For the rest of the run. reason is null when the agent closed the console itself.
    private void FallBack(string? reason)
    {
        List<PendingQuestion> pending;
        ConsoleState? state;

        lock (_lock)
        {
            if (_fellBack)
            {
                return;
            }

            _fellBack = true;
            _connected = false;
            pending = [.. _pending.Values];
            _pending.Clear();
            _control.Clear();
            _lines.Clear();
            state = _state;
        }

        if (reason is not null)
        {
            _log.Warning($"The graphical console {reason}. The agent carries on with the text console.");
        }

        if (state is not null)
        {
            _fallback.Show(state);
        }

        foreach (PendingQuestion question in pending)
        {
            question.Answer.TrySetResult(new PendingAnswer(true, null));
        }
    }

    private static string ExitCode(IConsoleProcess process) =>
        process.Exited.IsCompletedSuccessfully
            ? string.Create(CultureInfo.InvariantCulture, $"exit code 0x{process.Exited.Result:X8}")
            : "no exit code";

    private static string Seconds(TimeSpan timeout) => string.Create(CultureInfo.InvariantCulture, $"{timeout.TotalSeconds:0.#} s");

    private sealed class PendingQuestion(int id, ConsoleQuestion question)
    {
        public int Id => id;

        public ConsoleQuestion Question => question;

        public TaskCompletionSource<PendingAnswer> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // FellBack says that the text console is to ask instead.
    private readonly record struct PendingAnswer(bool FellBack, ConsoleAnswer? Answer);
}
