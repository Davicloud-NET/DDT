// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// The question a console over a pipe has open, for the graphical console in Windows PE and the console of DDT's session
// alike. The agent asks one question at a time, so the slot holds one: a question asked while another is open takes its
// place, and the other is withdrawn. Each question gets an id never used before, and stays open until the console
// answers it, the asker no longer needs it, or the slot closes for good. While no console is connected the question
// waits, and each console that connects gets it.
//
// send takes what the connected console is to get, a question or a withdrawal. The slot calls it under its own lock,
// so the order of the messages is the order of the changes; a console therefore never calls the slot while it holds a
// lock that send takes. The slot never looks at an answer: answers may carry passwords, and nothing here logs.
public sealed class QuestionSlot(Action<ConsoleMessage> send)
{
    private readonly Lock _lock = new();
    private OpenQuestion? _open;
    private int _lastId;
    private bool _connected;
    private bool _closed;

    // The id of the question open now, or null.
    public int? OpenId
    {
        get
        {
            lock (_lock)
            {
                return _open?.Id;
            }
        }
    }

    // Completes with the console's answer; with no answer once cancellationToken is cancelled, which withdraws the
    // question, or once the next question takes its place; and as Gone once the slot has closed, so the asker can ask
    // somewhere else.
    public async Task<QuestionOutcome> AskAsync(ConsoleQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        OpenQuestion open;
        OpenQuestion? replaced;

        lock (_lock)
        {
            if (_closed)
            {
                return QuestionOutcome.NoConsole;
            }

            replaced = _open;

            if (replaced is not null && _connected)
            {
                send(new WithdrawMessage(replaced.Id));
            }

            open = new OpenQuestion(++_lastId, question);
            _open = open;

            if (_connected)
            {
                send(new QuestionMessage(open.Id, question));
            }
        }

        replaced?.Outcome.TrySetResult(QuestionOutcome.Unanswered);

        using (cancellationToken.Register(() => Withdraw(open)))
        {
            return await open.Outcome.Task.ConfigureAwait(false);
        }
    }

    // A console has said hello, and gets the open question, after whatever the console sends it first.
    public void Connected()
    {
        lock (_lock)
        {
            if (_closed)
            {
                return;
            }

            _connected = true;

            if (_open is { } open)
            {
                send(new QuestionMessage(open.Id, open.Question));
            }
        }
    }

    // The console went away. The question stays open for the next one.
    public void Disconnected()
    {
        lock (_lock)
        {
            _connected = false;
        }
    }

    // The console's answer to question id. False where that is not the open question, as for an answer to one withdrawn
    // or replaced meanwhile, which is left alone.
    public bool Answer(int id, ConsoleAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        OpenQuestion? open;

        lock (_lock)
        {
            if (_open is not { } current || current.Id != id)
            {
                return false;
            }

            open = current;
            _open = null;
        }

        open.Outcome.TrySetResult(new QuestionOutcome(answer, Gone: false));

        return true;
    }

    // For good, as when the console is gone for the rest of the run: the open question and every one asked after this
    // come back as Gone.
    public void Close()
    {
        OpenQuestion? open;

        lock (_lock)
        {
            _closed = true;
            _connected = false;
            open = _open;
            _open = null;
        }

        open?.Outcome.TrySetResult(QuestionOutcome.NoConsole);
    }

    private void Withdraw(OpenQuestion open)
    {
        lock (_lock)
        {
            // Answered, replaced or closed meanwhile.
            if (_open != open)
            {
                return;
            }

            _open = null;

            if (_connected)
            {
                send(new WithdrawMessage(open.Id));
            }
        }

        open.Outcome.TrySetResult(QuestionOutcome.Unanswered);
    }

    private sealed class OpenQuestion(int id, ConsoleQuestion question)
    {
        public int Id => id;

        public ConsoleQuestion Question => question;

        public TaskCompletionSource<QuestionOutcome> Outcome { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

// How a question ended: with the console's answer, without one, or Gone, when the console went away for good before it
// answered.
public readonly record struct QuestionOutcome(ConsoleAnswer? Answer, bool Gone)
{
    public static QuestionOutcome Unanswered => new(null, Gone: false);

    public static QuestionOutcome NoConsole => new(null, Gone: true);
}
