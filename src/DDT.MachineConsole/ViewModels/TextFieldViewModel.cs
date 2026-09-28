// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// A line of text, starting with the default.
public sealed class TextFieldViewModel : InputFieldViewModel
{
    private string _text;

    public TextFieldViewModel(Localizer localizer, ConsoleInput input, Action changed)
        : base(localizer, input, changed)
    {
        _text = input.Default ?? string.Empty;
    }

    public string Text
    {
        get => _text;
        set
        {
            if (Set(ref _text, value ?? string.Empty))
            {
                Changed();
            }
        }
    }

    // 0 bounds nothing, as the text box takes it.
    public int MaxLength => Input.MaxLength ?? 0;

    public override bool IsAnswered => IsOptional || !string.IsNullOrWhiteSpace(Text);

    public override ConsoleInputValue Value() => new(Input.Name, Text.Trim());
}
