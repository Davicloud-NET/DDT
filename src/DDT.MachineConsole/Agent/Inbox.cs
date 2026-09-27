// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using DDT.ConsoleProtocol;

namespace DDT.MachineConsole.Agent;

// Between the thread that reads the pipe and the UI thread: the reader drops every message here and goes on reading,
// and the UI takes them in order, many at a time after a burst such as the log lines from before the console started.
public sealed class Inbox(Action<Action> post)
{
    private readonly ConcurrentQueue<ConsoleMessage> _messages = new();
    private readonly Lock _lock = new();
    private Action<ConsoleMessage>? _receiver;
    private LinkEnd? _end;
    private Action<LinkEnd>? _ended;
    private bool _posted;

    // From the reading thread.
    public void Add(ConsoleMessage message)
    {
        _messages.Enqueue(message);
        Schedule();
    }

    public void End(LinkEnd end)
    {
        lock (_lock)
        {
            _end = end;
        }

        Schedule();
    }

    // From the UI thread, once there is a screen for the messages.
    public void Deliver(Action<ConsoleMessage> receiver, Action<LinkEnd> ended)
    {
        lock (_lock)
        {
            _receiver = receiver;
            _ended = ended;
        }

        Schedule();
    }

    private void Schedule()
    {
        lock (_lock)
        {
            if (_posted || _receiver is null)
            {
                return;
            }

            _posted = true;
        }

        post(Drain);
    }

    private void Drain()
    {
        Action<ConsoleMessage>? receiver;
        Action<LinkEnd>? ended;

        lock (_lock)
        {
            _posted = false;
            receiver = _receiver;
            ended = _ended;
        }

        while (receiver is not null && _messages.TryDequeue(out ConsoleMessage? message))
        {
            receiver(message);
        }

        LinkEnd? end;

        lock (_lock)
        {
            // Lines that came after this drain began are taken first, by the drain they scheduled.
            end = _messages.IsEmpty ? _end : null;
        }

        if (end is { } linkEnd)
        {
            ended?.Invoke(linkEnd);
        }
    }
}
