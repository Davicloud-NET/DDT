// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// Any number of a few options, shown as a row of keys that each toggle on and off. The answer lists the chosen values
// in the order shown.
public sealed class MultiChoiceFieldViewModel : InputFieldViewModel
{
    public MultiChoiceFieldViewModel(Localizer localizer, ConsoleInput input, Action changed)
        : base(localizer, input, changed)
    {
        HashSet<string> chosen = [.. (input.Default ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        Items =
        [
            .. input.Choices.Select(choice => new ChoiceItem(choice.Value, () => choice.Label ?? choice.Value, Changed)
            {
                IsChosen = chosen.Contains(choice.Value),
            }),
        ];
    }

    public IReadOnlyList<ChoiceItem> Items { get; }

    public string Hint => L.T("Choose any of these. Space turns one on or off.");

    public override bool IsAnswered => IsOptional || Items.Any(item => item.IsChosen);

    public override ConsoleInputValue Value() =>
        new(Input.Name, string.Join(';', Items.Where(item => item.IsChosen).Select(item => item.Value)));

    public override void Refresh()
    {
        foreach (ChoiceItem item in Items)
        {
            item.Refresh();
        }

        base.Refresh();
    }
}
