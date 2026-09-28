// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// A question answered by typing a word, ERASE or ANYWAY, so a stray key or click never erases a disk. It sends Typed,
// not Word, so the agent checks what was actually typed.
public abstract class TypedWordViewModel : QuestionViewModel
{
    private string _typed = string.Empty;

    protected TypedWordViewModel(Localizer localizer, int id, string word, Action<int, ConsoleAnswer> answer)
        : base(localizer, id, answer)
    {
        ArgumentNullException.ThrowIfNull(word);

        Word = word;
    }

    public string Word { get; }

    public string Typed
    {
        get => _typed;
        set
        {
            if (Set(ref _typed, value))
            {
                Raise(nameof(IsTyped));
                Raise(nameof(ShowsWordHint));
                RefreshCommands();
            }
        }
    }

    public bool IsTyped => string.Equals(Typed, Word, StringComparison.Ordinal);

    // The typed text is as long as the word but doesn't match it, like the word in lower case.
    public bool ShowsWordHint => Typed.Length >= Word.Length && !IsTyped;

    public string WordHint => F("Type {word} in capitals, exactly as shown.", ("word", Word));

    public override bool CanSubmit => IsTyped;

    public override bool CanGoBack => true;

    protected override ConsoleAnswer? Answer() => IsTyped ? new ConsoleAnswer(Text: Typed) : null;

    protected override void Sent() => Typed = string.Empty;
}
