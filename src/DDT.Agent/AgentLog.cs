// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Consoles;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// Writes to the console immediately, because in Windows PE the console is all an operator at the
// machine has, and queues the same lines for the server until they are delivered. Dated lines are for a file, which
// someone reads days later beside Windows' own logs; the console, read as the lines appear, keeps only the time.
// Both are in UTC. The text console gets every line even while the graphical one shows them, so it has them all should
// the graphical one go away.
public sealed class AgentLog(TimeProvider timeProvider, TextWriter console, bool datedLines = false)
{
    private const int MaxQueuedLines = 2000;

    private readonly string _timeFormat = datedLines ? "yyyy-MM-dd HH:mm:ss 'UTC'" : "HH:mm:ss";

    private readonly Lock _lock = new();
    private readonly List<(long Sequence, AgentLogLine Line)> _pending = [];
    private readonly List<string> _heldConsoleLines = [];
    private long _nextSequence;
    private int _dropped;
    private bool _consoleHeld;
    private Guid? _stepId;
    private IMachineConsole? _machineConsole;

    // A console that shows the log itself, such as the graphical one, which gets every line from now on, in order.
    public IMachineConsole? MachineConsole
    {
        get
        {
            lock (_lock)
            {
                return _machineConsole;
            }
        }

        set
        {
            lock (_lock)
            {
                _machineConsole = value;
            }
        }
    }

    public void Information(string message) => Write(AgentLogLevel.Information, message);

    public void Warning(string message) => Write(AgentLogLevel.Warning, message);

    public void Error(string message) => Write(AgentLogLevel.Error, message);

    // While someone types at a prompt, lines are kept off the console so they do not split the typed line, and
    // they appear once the prompt is done.
    public void HoldConsole(string prompt)
    {
        lock (_lock)
        {
            _consoleHeld = true;
            console.Write(prompt);
        }
    }

    public void ReleaseConsole()
    {
        lock (_lock)
        {
            _consoleHeld = false;

            foreach (string line in _heldConsoleLines)
            {
                console.WriteLine(line);
            }

            _heldConsoleLines.Clear();
        }
    }

    // The run's step that is running, which every line written meanwhile names, so the server can show a step's
    // lines. Null between steps.
    public Guid? StepId
    {
        get
        {
            lock (_lock)
            {
                return _stepId;
            }
        }

        set
        {
            lock (_lock)
            {
                _stepId = value;
            }
        }
    }

    public int QueuedLines
    {
        get
        {
            lock (_lock)
            {
                return _pending.Count;
            }
        }
    }

    public async Task FlushAsync(IAgentServer server, Guid machineId, string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);

        List<AgentLogLine> batch = [];
        long lastSequence;
        int dropped;

        lock (_lock)
        {
            dropped = _dropped;

            // The count of lost lines travels in the batch rather than the queue, so it cannot itself be
            // evicted, and it is only cleared once a batch carrying it has been accepted.
            if (dropped > 0)
            {
                batch.Add(new AgentLogLine(
                    timeProvider.GetUtcNow(),
                    AgentLogLevel.Warning,
                    $"{dropped} log lines were dropped while the server could not take them."));
            }

            int take = AgentLimits.MaxLinesPerBatch - batch.Count;
            batch.AddRange(_pending.Take(take).Select(entry => entry.Line));
            lastSequence = _pending.Take(take).Select(entry => entry.Sequence).DefaultIfEmpty(-1).Last();
        }

        if (batch.Count == 0)
        {
            return;
        }

        // The server corrects the lines' times by how far this clock is from its own when the batch arrives.
        await server.SendLogAsync(machineId, token, new AgentLogBatch(batch, timeProvider.GetUtcNow()), cancellationToken).ConfigureAwait(false);

        // Removed by sequence, not position: lines evicted while the batch was on its way have shifted the
        // queue, and must not take unsent lines with them.
        lock (_lock)
        {
            _pending.RemoveAll(entry => entry.Sequence <= lastSequence);
            _dropped -= dropped;
        }
    }

    private void Write(AgentLogLevel level, string message)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string text = $"{now.ToString(_timeFormat, CultureInfo.InvariantCulture)} {LevelLabel(level)} {message}";

        lock (_lock)
        {
            if (_consoleHeld)
            {
                _heldConsoleLines.Add(text);
            }
            else
            {
                console.WriteLine(text);
            }

            if (_pending.Count >= MaxQueuedLines)
            {
                _pending.RemoveAt(0);
                _dropped++;
            }

            _pending.Add((_nextSequence++, new AgentLogLine(now, level, message, _stepId)));
            _machineConsole?.Write(new ConsoleLogLine(now, ConsoleValues.ToConsole(level), message, _stepId));
        }
    }

    private static string LevelLabel(AgentLogLevel level) => level switch
    {
        AgentLogLevel.Warning => "WARN ",
        AgentLogLevel.Error => "ERROR",
        _ => "INFO ",
    };
}
