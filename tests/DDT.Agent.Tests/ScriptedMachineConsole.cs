// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.ConsoleProtocol;

namespace DDT.Agent.Tests;

// A console as the graphical one sees the agent, answering each question at once with the next answer of the script,
// which is given the question. Once the script runs out it waits, as a technician who has walked away does, until the
// question is withdrawn. It keeps every state, line and question it got; the run's heartbeat shows from its own thread.
internal sealed class ScriptedMachineConsole(params Func<ConsoleQuestion, ConsoleAnswer>[] answers) : IMachineConsole
{
    private readonly Lock _lock = new();
    private readonly Queue<Func<ConsoleQuestion, ConsoleAnswer>> _answers = new(answers);
    private readonly List<ConsoleQuestion> _questions = [];
    private readonly List<ConsoleState> _states = [];
    private readonly List<ConsoleLogLine> _lines = [];
    private int _withdrawn;

    public bool CanAsk { get; init; } = true;

    public List<ConsoleQuestion> Questions
    {
        get
        {
            lock (_lock)
            {
                return [.. _questions];
            }
        }
    }

    public List<ConsoleState> States
    {
        get
        {
            lock (_lock)
            {
                return [.. _states];
            }
        }
    }

    public List<ConsoleLogLine> Lines
    {
        get
        {
            lock (_lock)
            {
                return [.. _lines];
            }
        }
    }

    public int Withdrawn
    {
        get
        {
            lock (_lock)
            {
                return _withdrawn;
            }
        }
    }

    public static Func<ConsoleQuestion, ConsoleAnswer> Typed(string text) => _ => new ConsoleAnswer(Text: text);

    public static Func<ConsoleQuestion, ConsoleAnswer> Back { get; } = _ => new ConsoleAnswer(Back: true);

    public static Func<ConsoleQuestion, ConsoleAnswer> Disk(int number) => _ => new ConsoleAnswer(DiskNumber: number);

    // The sequence of that name on the list the question shows.
    public static Func<ConsoleQuestion, ConsoleAnswer> Sequence(string name) =>
        question => new ConsoleAnswer(SequenceId: ((SequenceQuestion)question).Sequences.Single(option => option.Name == name).Id);

    public void Show(ConsoleState state)
    {
        lock (_lock)
        {
            _states.Add(state);
        }
    }

    public void Write(ConsoleLogLine line)
    {
        lock (_lock)
        {
            _lines.Add(line);
        }
    }

    public Task<ConsoleAnswer?> AskAsync(ConsoleQuestion question, CancellationToken cancellationToken)
    {
        Func<ConsoleQuestion, ConsoleAnswer>? answer;

        lock (_lock)
        {
            _questions.Add(question);
            _answers.TryDequeue(out answer);
        }

        if (answer is not null)
        {
            return Task.FromResult<ConsoleAnswer?>(answer(question));
        }

        TaskCompletionSource<ConsoleAnswer?> waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() =>
        {
            lock (_lock)
            {
                _withdrawn++;
            }

            waiting.TrySetResult(null);
        });

        return waiting.Task;
    }
}
