// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// What a console over a pipe gets next, under one lock and in this order: the newest state, the log lines, then the
// question slot's messages. The state and the newest lines wait for a console that connects later.
internal sealed class ConsoleOutbox
{
    // The lines kept for a console that connects late, and for one that reads slowly.
    private const int BacklogLines = 500;
    private const int MaxUnsentLines = 5000;

    private readonly Lock _lock = new();
    private readonly Queue<ConsoleLogLine> _recent = new();
    private readonly Queue<ConsoleLogLine> _unsent = new();
    private readonly Queue<ConsoleMessage> _control = new();
    private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ConsoleState? _state;
    private bool _stateUnsent;
    private bool _connected;
    private bool _closing;
    private bool _shut;

    public bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _connected;
            }
        }
    }

    public bool IsClosing
    {
        get
        {
            lock (_lock)
            {
                return _closing;
            }
        }
    }

    // True once Shut, when nothing waits here for a console any more.
    public bool IsShut
    {
        get
        {
            lock (_lock)
            {
                return _shut;
            }
        }
    }

    // False once shut, so the caller shows the state elsewhere.
    public bool TryShow(ConsoleState state)
    {
        lock (_lock)
        {
            if (_shut)
            {
                return false;
            }

            _state = state;

            if (_connected)
            {
                _stateUnsent = true;
                _wake.TrySetResult();
            }

            return true;
        }
    }

    // False once shut, so the caller writes the line elsewhere.
    public bool TryWrite(ConsoleLogLine line)
    {
        lock (_lock)
        {
            if (_shut)
            {
                return false;
            }

            Enqueue(_recent, line, BacklogLines);

            if (_connected)
            {
                Enqueue(_unsent, line, MaxUnsentLines);
                _wake.TrySetResult();
            }

            return true;
        }
    }

    // A question or a withdrawal from the slot, for the console that is connected.
    public void Send(ConsoleMessage message)
    {
        lock (_lock)
        {
            if (_connected)
            {
                _control.Enqueue(message);
                _wake.TrySetResult();
            }
        }
    }

    // A console said hello: the state and the lines kept go out first.
    public void Connect()
    {
        lock (_lock)
        {
            _connected = true;
            _stateUnsent = _state is not null;

            foreach (ConsoleLogLine line in _recent)
            {
                _unsent.Enqueue(line);
            }

            _wake.TrySetResult();
        }
    }

    // What the console has not read yet is dropped; the next console gets the state and the lines kept.
    public void Disconnect()
    {
        lock (_lock)
        {
            _connected = false;
            _stateUnsent = false;
            _unsent.Clear();
            _control.Clear();
        }
    }

    // For good. False when it was shut already; otherwise state is the last one shown, for the console that takes over.
    public bool TryShut(out ConsoleState? state)
    {
        lock (_lock)
        {
            state = _state;

            if (_shut)
            {
                return false;
            }

            _shut = true;
            _connected = false;
            _control.Clear();
            _unsent.Clear();
            _recent.Clear();

            return true;
        }
    }

    // The sender stops once everything left is sent. False when closing already; connected says whether a console is.
    public bool TryBeginClosing(out bool connected)
    {
        lock (_lock)
        {
            connected = _connected;

            if (_closing)
            {
                return false;
            }

            _closing = true;
            _wake.TrySetResult();

            return true;
        }
    }

    // The messages due, in order, or none and wake to wait for; null once closing with nothing left to send.
    public List<ConsoleMessage>? Take(out Task wake)
    {
        List<ConsoleMessage> messages = [];

        lock (_lock)
        {
            if (_stateUnsent && _state is { } state)
            {
                messages.Add(new StateMessage(state));
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
                    wake = Task.CompletedTask;

                    return null;
                }

                _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            wake = _wake.Task;

            return messages;
        }
    }

    private static void Enqueue(Queue<ConsoleLogLine> lines, ConsoleLogLine line, int limit)
    {
        lines.Enqueue(line);

        while (lines.Count > limit)
        {
            lines.Dequeue();
        }
    }
}
