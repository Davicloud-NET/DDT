// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// Which task sequence to run, chosen by what it is: the list with the arrow keys, Enter to run the one selected. The
// one an assignment rule suggests comes first and is selected.
public sealed class SequenceChoiceViewModel : QuestionViewModel
{
    private SequenceItem? _selected;

    public SequenceChoiceViewModel(Localizer localizer, int id, SequenceQuestion question, Action<int, ConsoleAnswer> answer)
        : base(localizer, id, answer)
    {
        ArgumentNullException.ThrowIfNull(question);

        Question = question;
        Items = [.. question.Sequences.Select(sequence => new SequenceItem(localizer, sequence))];
        _selected = Items.FirstOrDefault(item => item.Option.Suggested) ?? Items.FirstOrDefault();
    }

    public SequenceQuestion Question { get; }

    public IReadOnlyList<SequenceItem> Items { get; }

    public SequenceItem? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                RefreshCommands();
            }
        }
    }

    public string Title => T("Choose a task sequence");

    public string Intro => T("These are the task sequences this machine can run. A sequence an assignment rule suggests comes first.");

    public string Empty => T("This machine can run no task sequence yet. Assign one on the Machines page.");

    public bool IsEmpty => Items.Count == 0;

    public string MoveHint => T("Choose");

    public string SubmitLabel => T("Run the task sequence");

    public override bool CanSubmit => Selected is not null;

    public override void Refresh()
    {
        foreach (SequenceItem item in Items)
        {
            item.Refresh();
        }

        base.Refresh();
    }

    protected override ConsoleAnswer? Answer() => Selected is { } item ? new ConsoleAnswer(SequenceId: item.Option.Id) : null;
}

// A sequence in the list, with its flags as tags.
public sealed class SequenceItem(Localizer localizer, SequenceOption option) : ObservableObject
{
    public SequenceOption Option => option;

    public string Name => option.Name;

    public string? Description => string.IsNullOrWhiteSpace(option.Description) ? null : option.Description;

    public bool HasDescription => Description is not null;

    public IReadOnlyList<Tag> Tags
    {
        get
        {
            List<Tag> tags = [];

            if (option.Suggested)
            {
                tags.Add(Tag.Of(localizer.T("Suggested"), TagTone.Ok));
            }

            if (option.ErasesDisk)
            {
                tags.Add(Tag.Of(localizer.T("Erases a disk"), TagTone.Idle));
            }

            if (option.NeedsComputerName)
            {
                tags.Add(Tag.Of(localizer.T("Asks for a name"), TagTone.Idle));
            }

            if (option.RequiredBytes > 0)
            {
                tags.Add(Tag.Of(localizer.F("Needs {size}", ("size", Say.Bytes(localizer, option.RequiredBytes))), TagTone.Idle));
            }

            if (option.NotSignedForSecureBoot)
            {
                tags.Add(Tag.Of(localizer.T("Not for Secure Boot"), TagTone.Attention));
            }

            if (option.NotTrustedHere)
            {
                tags.Add(Tag.Of(localizer.T("Not trusted by this machine"), TagTone.Attention));
            }

            return tags;
        }
    }

    public void Refresh() => Raise(nameof(Tags));
}
