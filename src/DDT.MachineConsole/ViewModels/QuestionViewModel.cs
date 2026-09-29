// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// After an answer the question stays, with its keys off, until the agent asks again or moves on. The agent checks every
// answer itself.
public abstract class QuestionViewModel : ScreenViewModel
{
    private readonly Action<int, ConsoleAnswer> _answer;
    private bool _isSending;

    protected QuestionViewModel(Localizer localizer, int id, Action<int, ConsoleAnswer> answer)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        Id = id;
        _answer = answer;
        SubmitCommand = new Command(Submit, () => !IsSending && CanSubmit);
        BackCommand = new Command(Back, () => !IsSending && CanGoBack);
    }

    public int Id { get; private set; }

    // True from the answer until the agent asks again or moves on.
    public bool IsSending
    {
        get => _isSending;
        private set
        {
            if (Set(ref _isSending, value))
            {
                Raise(nameof(IsEditable));
                RefreshCommands();
            }
        }
    }

    public bool IsEditable => !IsSending;

    public Command SubmitCommand { get; }

    public Command BackCommand { get; }

    public abstract bool CanSubmit { get; }

    public virtual bool CanGoBack => false;

    // Takes the next question of the same kind in place and keeps what was typed. Returns false if it needs a new
    // screen.
    public virtual bool Accept(int id, ConsoleQuestion question) => false;

    // Gets each state of the agent while the question is on screen, for questions that show part of it.
    public virtual void Update(ConsoleState state)
    {
    }

    // What the screen sends for its current input, or null when there is nothing to send yet.
    protected abstract ConsoleAnswer? Answer();

    protected virtual ConsoleAnswer? BackAnswer() => new(Back: true);

    // After sending, clears what was typed and must not stay on the screen, like a password.
    protected virtual void Sent()
    {
    }

    protected void Reopen(int id)
    {
        Id = id;
        IsSending = false;
    }

    protected void RefreshCommands()
    {
        SubmitCommand.Refresh();
        BackCommand.Refresh();
    }

    private void Submit()
    {
        if (IsSending || !CanSubmit || Answer() is not { } answer)
        {
            return;
        }

        Send(answer);
    }

    private void Back()
    {
        if (IsSending || !CanGoBack || BackAnswer() is not { } answer)
        {
            return;
        }

        Send(answer);
    }

    private void Send(ConsoleAnswer answer)
    {
        IsSending = true;
        _answer(Id, answer);
        Sent();
    }
}
