// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// The one question a console over a pipe has open: a new one takes its place, and it waits while no console is
// connected. send gets the console's messages under the slot's lock, so they keep the order of the changes, and a
// console must not call the slot while it holds a lock send takes. Answers may hold passwords, so nothing here logs.
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

    // The console's answer, or none once cancelled, which withdraws the question, or once the next one takes its place.
    // Gone once the slot has closed, so the asker can ask elsewhere.
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
