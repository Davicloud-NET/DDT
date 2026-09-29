// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// Nothing is selected at first, so Enter alone never picks a disk to erase.
public sealed class DiskChoiceViewModel : QuestionViewModel
{
    private DiskItem? _selected;

    public DiskChoiceViewModel(Localizer localizer, int id, DiskQuestion question, Action<int, ConsoleAnswer> answer)
        : base(localizer, id, answer)
    {
        ArgumentNullException.ThrowIfNull(question);

        Question = question;
        Items = [.. question.Disks.Select(disk => new DiskItem(localizer, disk))];
    }

    public DiskQuestion Question { get; }

    public IReadOnlyList<DiskItem> Items { get; }

    public DiskItem? Selected
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

    public string Title => T("Choose the disk");

    public string Intro => F(
        "{sequence} erases the disk you choose. This machine has more than one disk it can install on.",
        ("sequence", Question.SequenceName));

    public string SubmitLabel => T("Use this disk");

    public string BackLabel => T("Back to the task sequences");

    public string MoveHint => T("Choose");

    public override bool CanSubmit => Selected is not null;

    public override bool CanGoBack => true;

    public override void Refresh()
    {
        foreach (DiskItem item in Items)
        {
            item.Refresh();
        }

        base.Refresh();
    }

    protected override ConsoleAnswer? Answer() => Selected is { } item ? new ConsoleAnswer(DiskNumber: item.Disk.Number) : null;
}
