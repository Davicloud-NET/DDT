// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// One field of a sequence's inputs: its label, whether it may stay empty, the help its author wrote and the agent's
// words about the answer before. changed tells the page that the answer changed, so its key can say whether all of them
// can go.
public abstract class InputFieldViewModel : ObservableObject
{
    private readonly Action _changed;

    protected InputFieldViewModel(Localizer localizer, ConsoleInput input, Action changed)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(changed);

        L = localizer;
        Input = input;
        _changed = changed;
    }

    public ConsoleInput Input { get; private set; }

    public string Label => Input.Label;

    public bool IsOptional => !Input.Required;

    public string OptionalLabel => L.T("Optional");

    public string? Help => string.IsNullOrWhiteSpace(Input.Help) ? null : Input.Help;

    public bool HasHelp => Help is not null;

    // The agent's words about the answer before.
    public string? Error => Input.Error;

    public bool HasError => !string.IsNullOrEmpty(Input.Error);

    // True where the field may be sent as it is: it has an answer, or it may stay empty.
    public abstract bool IsAnswered { get; }

    protected Localizer L { get; }

    public abstract ConsoleInputValue Value();

    // Whether this field can show the input asked again and keep what was typed: the same kind, with the same choices.
    public bool Takes(ConsoleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input.Kind == Input.Kind
            && input.Choices.Select(choice => choice.Value).SequenceEqual(Input.Choices.Select(choice => choice.Value), StringComparer.Ordinal);
    }

    // The input asked again, with what was wrong with it. A password is typed again in any case.
    public InputFieldViewModel Asked(ConsoleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        Input = input;
        ForgetSecrets();
        RaiseAll();

        return this;
    }

    // Once the answers are sent, what must not stay on the screen goes.
    public virtual void ForgetSecrets()
    {
    }

    public virtual void Refresh() => RaiseAll();

    protected void Changed() => _changed();
}

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

// One of a few, as a row of keys with the chosen one filled: a Choice input's choices, or Yes and No. The arrow keys
// choose. Only the default is chosen at first, so an input without one is answered on purpose.
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

// Any of a few, as a row of keys that each turn on and off. The answer names the chosen values in the order shown.
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

// An account for the run: a user name and its password, which is masked, sent once and then forgotten.
public sealed class AccountFieldViewModel : InputFieldViewModel
{
    private string _userName;
    private string _password = string.Empty;

    public AccountFieldViewModel(Localizer localizer, ConsoleInput input, string? keyboardLayout, Action changed)
        : base(localizer, input, changed)
    {
        _userName = input.Default ?? string.Empty;
        KeyboardLayout = keyboardLayout;
    }

    public string UserName
    {
        get => _userName;
        set
        {
            if (Set(ref _userName, value ?? string.Empty))
            {
                Changed();
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (Set(ref _password, value ?? string.Empty))
            {
                Changed();
            }
        }
    }

    public string UserNameLabel => L.T("User name");

    public string PasswordLabel => L.T("Password");

    // What the account is for, that its password stays with DDT for the run, and the keyboard layout it is typed with.
    public string Note => HasKeyboardLayout ? $"{Keeping} {KeyboardHint}" : Keeping;

    public string? KeyboardLayout { get; private set; }

    public bool HasKeyboardLayout => !string.IsNullOrEmpty(KeyboardLayout);

    public string KeyboardHint => L.F(
        "The keyboard layout is {layout}. A password typed with another layout would be wrong.",
        ("layout", KeyboardLayout ?? string.Empty));

    // Both or, where the account may stay unanswered, neither.
    public override bool IsAnswered => (HasUserName && Password.Length > 0) || (IsOptional && !HasUserName && Password.Length == 0);

    private bool HasUserName => !string.IsNullOrWhiteSpace(UserName);

    private string Keeping => string.IsNullOrWhiteSpace(Input.Domain)
        ? L.T("DDT keeps it for this run only and never shows it.")
        : L.F("For {domain}. DDT keeps it for this run only and never shows it.", ("domain", Input.Domain));

    public override ConsoleInputValue Value() =>
        HasUserName ? new ConsoleInputValue(Input.Name, null, UserName.Trim(), Password) : new ConsoleInputValue(Input.Name, null);

    public override void ForgetSecrets() => Password = string.Empty;

    public void KeyboardLayoutIs(string? layout)
    {
        KeyboardLayout = layout;
        Raise(nameof(KeyboardLayout));
        Raise(nameof(HasKeyboardLayout));
        Raise(nameof(KeyboardHint));
        Raise(nameof(Note));
    }
}

// A key of a choice. IsChosen is for the keys that turn on and off; a single choice is its list's selection.
public sealed class ChoiceItem(string value, Func<string> label, Action? changed = null) : ObservableObject
{
    private bool _isChosen;

    public string Value => value;

    public string Label => label();

    public bool IsChosen
    {
        get => _isChosen;
        set
        {
            if (Set(ref _isChosen, value))
            {
                changed?.Invoke();
            }
        }
    }

    public void Refresh() => Raise(nameof(Label));
}
