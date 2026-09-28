// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// The last confirmation before the sequence erases the disk.
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
