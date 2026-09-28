// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// The agent checks the name and asks again with what was wrong. The field starts with the name the machine gets when
// none is typed, so Continue keeps it.
public sealed class ComputerNameViewModel : QuestionViewModel
{
    private ComputerNameQuestion _question;
    private string _name;

    public ComputerNameViewModel(Localizer localizer, int id, ComputerNameQuestion question, Action<int, ConsoleAnswer> answer)
        : base(localizer, id, answer)
    {
        ArgumentNullException.ThrowIfNull(question);

        _question = question;
        _name = question.Name ?? string.Empty;
    }

    public ComputerNameQuestion Question => _question;

    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, value))
            {
                RefreshCommands();
            }
        }
    }

    public int MaxLength => _question.MaxLength;

    public string Title => T("Name this machine");

    public string Intro => F("{sequence} needs a computer name for this machine.", ("sequence", _question.SequenceName));

    public string Label => T("Computer name");

    public string Rules => F(
        "At most {max} characters: the letters A to Z, digits and hyphens. Not only digits, and no hyphen first.",
        ("max", L.Number(_question.MaxLength)));

    // The agent's words about the name before.
    public string? Error => _question.Error;

    public bool HasError => !string.IsNullOrEmpty(_question.Error);

    public string SubmitLabel => T("Continue");

    public string BackLabel => T("Back to the task sequences");

    public override bool CanSubmit => !string.IsNullOrWhiteSpace(Name);

    public override bool CanGoBack => true;

    public override bool Accept(int id, ConsoleQuestion question)
    {
        if (question is not ComputerNameQuestion next)
        {
            return false;
        }

        _question = next;
        Reopen(id);
        RaiseAll();

        return true;
    }

    protected override ConsoleAnswer? Answer() => new(Text: Name.Trim());
}
