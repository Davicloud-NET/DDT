// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// The sequence an assignment rule suggests comes first and is selected, so Enter alone runs it.
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
