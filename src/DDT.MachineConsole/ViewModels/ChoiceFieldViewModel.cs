// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// A Choice or YesNo input. Only the default starts selected, so an input without a default is answered on purpose.
public sealed class ChoiceFieldViewModel : InputFieldViewModel
{
    private ChoiceItem? _selected;

    public ChoiceFieldViewModel(Localizer localizer, ConsoleInput input, Action changed)
        : base(localizer, input, changed)
    {
        Items = input.Kind == ConsoleInputKind.YesNo
            ? [new ChoiceItem("true", () => L.T("Yes")), new ChoiceItem("false", () => L.T("No"))]
            : [.. input.Choices.Select(choice => new ChoiceItem(choice.Value, () => choice.Label ?? choice.Value))];
        _selected = Items.FirstOrDefault(item => string.Equals(item.Value, input.Default, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ChoiceItem> Items { get; }

    public ChoiceItem? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                Changed();
            }
        }
    }

    public override bool IsAnswered => IsOptional || Selected is not null;

    public override ConsoleInputValue Value() => new(Input.Name, Selected?.Value);

    public override void Refresh()
    {
        foreach (ChoiceItem item in Items)
        {
            item.Refresh();
        }

        base.Refresh();
    }
}
