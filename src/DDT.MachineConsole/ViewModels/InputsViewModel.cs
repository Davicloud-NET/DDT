// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// A sequence's inputs on one page. The agent checks them and asks again with what was wrong. Each field keeps what was
// typed, except a password. Nothing typed here is ever logged.
public sealed class InputsViewModel : QuestionViewModel
{
    private readonly bool _canGoBack;
    private InputsQuestion _question;
    private IReadOnlyList<InputFieldViewModel> _fields;

    public InputsViewModel(
        Localizer localizer,
        int id,
        InputsQuestion question,
        bool canGoBack,
        string? keyboardLayout,
        Action<int, ConsoleAnswer> answer)
        : base(localizer, id, answer)
    {
        ArgumentNullException.ThrowIfNull(question);

        _question = question;
        _canGoBack = canGoBack;
        KeyboardLayout = keyboardLayout;
        _fields = [.. question.Inputs.Select(Field)];
    }

    public InputsQuestion Question => _question;

    public IReadOnlyList<InputFieldViewModel> Fields => _fields;

    public string? KeyboardLayout { get; private set; }

    public string Title => T("A few answers first");

    public string Intro => F("{sequence} asks these before it starts.", ("sequence", _question.SequenceName));

    // What was wrong with the answers as a whole, like the server rejecting them.
    public string? Error => _question.Error;

    public bool HasError => !string.IsNullOrEmpty(_question.Error);

    public bool HasChoices => _fields.Any(input => input is ChoiceFieldViewModel);

    public string NextFieldHint => T("Next field");

    public string ChooseHint => T("Choose");

    public string SubmitLabel => T("Continue");

    public string BackLabel => T("Back to the task sequences");

    public override bool CanSubmit => _fields.All(input => input.IsAnswered);

    public override bool CanGoBack => _canGoBack;

    // The same inputs asked again, with what was wrong. Each field keeps what was typed in it and shows its error.
    public override bool Accept(int id, ConsoleQuestion question)
    {
        if (question is not InputsQuestion next)
        {
            return false;
        }

        _question = next;

        // If the fields are the same, they stay on screen as they are. Otherwise they're laid out again.
        if (HasTheFieldsOf(next))
        {
            for (int index = 0; index < _fields.Count; index++)
            {
                _fields[index].Asked(next.Inputs[index]);
            }
        }
        else
        {
            Dictionary<string, InputFieldViewModel> before = _fields.ToDictionary(field => field.Input.Name, StringComparer.Ordinal);
            _fields =
            [
                .. next.Inputs.Select(input => before.TryGetValue(input.Name, out InputFieldViewModel? field) && field.Takes(input)
                    ? field.Asked(input)
                    : Field(input)),
            ];
        }

        Reopen(id);
        RaiseAll();
        RefreshCommands();

        return true;
    }

    public override void Update(ConsoleState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Machine.KeyboardLayout != KeyboardLayout)
        {
            KeyboardLayout = state.Machine.KeyboardLayout;

            foreach (AccountFieldViewModel account in _fields.OfType<AccountFieldViewModel>())
            {
                account.KeyboardLayoutIs(KeyboardLayout);
            }
        }
    }

    public override void Refresh()
    {
        foreach (InputFieldViewModel field in _fields)
        {
            field.Refresh();
        }

        base.Refresh();
    }

    protected override ConsoleAnswer? Answer() => new(Values: [.. _fields.Select(field => field.Value())]);

    protected override void Sent()
    {
        foreach (InputFieldViewModel field in _fields)
        {
            field.ForgetSecrets();
        }
    }

    private bool HasTheFieldsOf(InputsQuestion question) =>
        question.Inputs.Count == _fields.Count
        && question.Inputs.Select((input, index) => input.Name == _fields[index].Input.Name && _fields[index].Takes(input)).All(same => same);

    private InputFieldViewModel Field(ConsoleInput input) => input.Kind switch
    {
        ConsoleInputKind.Choice => new ChoiceFieldViewModel(L, input, RefreshCommands),
        ConsoleInputKind.YesNo => new ChoiceFieldViewModel(L, input, RefreshCommands),
        ConsoleInputKind.MultiChoice => new MultiChoiceFieldViewModel(L, input, RefreshCommands),
        ConsoleInputKind.Account => new AccountFieldViewModel(L, input, KeyboardLayout, RefreshCommands),
        _ => new TextFieldViewModel(L, input, RefreshCommands),
    };
}
