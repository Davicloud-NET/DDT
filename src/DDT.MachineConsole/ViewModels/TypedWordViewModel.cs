// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// A question the person answers by typing a word, ERASE or ANYWAY: a word typed on purpose keeps a stray key or click
// from erasing a disk. The key that goes on works only once the word is typed exactly, and sends what was typed, never
// the word on its own. Esc goes back to the list of sequences, and nothing is erased.
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

    // Something typed that is not the word yet, such as the word in small letters.
    public bool ShowsWordHint => Typed.Length >= Word.Length && !IsTyped;

    public string WordHint => F("Type {word} in capitals, exactly as shown.", ("word", Word));

    public override bool CanSubmit => IsTyped;

    public override bool CanGoBack => true;

    protected override ConsoleAnswer? Answer() => IsTyped ? new ConsoleAnswer(Text: Typed) : null;

    protected override void Sent() => Typed = string.Empty;
}

// The last word before the sequence erases the disk.
public sealed class EraseViewModel : TypedWordViewModel
{
    public EraseViewModel(Localizer localizer, int id, EraseQuestion question, Action<int, ConsoleAnswer> answer)
        : base(localizer, id, question?.Word ?? throw new ArgumentNullException(nameof(question)), answer)
    {
        Question = question;
        Disk = new DiskItem(localizer, question.Disk);
    }

    public EraseQuestion Question { get; }

    public DiskItem Disk { get; }

    public string Title => F("Erase disk {number}?", ("number", L.Number(Question.Disk.Number)));

    public Tag Tag => Tag.Of(T("Erases"), TagTone.Fail);

    public string Warning => F(
        "{sequence} erases everything on this disk, and it cannot be undone.",
        ("sequence", Question.SequenceName));

    public string DataWarning => Disk.HoldsData
        ? T("The disk has partitions, so it holds something now.")
        : T("The disk has no partitions.");

    public string Label => F("Type {word} to erase the disk", ("word", Word));

    public string SubmitLabel => T("Erase and continue");

    public string BackLabel => T("Back, erase nothing");

    public override void Refresh()
    {
        Disk.Refresh();
        base.Refresh();
    }
}

// The Secure Boot override: the disk image would not start with the Secure Boot this machine has on.
public sealed class SecureBootViewModel : TypedWordViewModel
{
    private readonly MicrosoftUefiCas? _trusted;

    public SecureBootViewModel(
        Localizer localizer,
        int id,
        SecureBootQuestion question,
        MicrosoftUefiCas? trusted,
        Action<int, ConsoleAnswer> answer)
        : base(localizer, id, question?.Word ?? throw new ArgumentNullException(nameof(question)), answer)
    {
        Question = question;
        _trusted = trusted;
    }

    public SecureBootQuestion Question { get; }

    public string Title => T("Write it despite Secure Boot?");

    public Tag Tag => Tag.Of(T("Secure Boot"), TagTone.Attention);

    public string Problem => Say.SecureBootProblem(L, Question);

    public IReadOnlyList<Fact> Facts
    {
        get
        {
            List<Fact> facts = [new(T("Task sequence"), Question.SequenceName)];

            if (Question.ImageName is { } image)
            {
                facts.Add(new Fact(T("Disk image"), image));
            }

            if (Question.Problem == SecureBootProblem.UntrustedCa)
            {
                facts.Add(new Fact(T("Signed under"), Say.Cas(L, Question.SignedUnder)));
            }

            facts.Add(new Fact(T("This machine trusts"), Say.Cas(L, _trusted)));

            return facts;
        }
    }

    public string Label => F("Type {word} to write it all the same", ("word", Word));

    public string SubmitLabel => T("Write it anyway");

    public string BackLabel => T("Back, write nothing");
}
