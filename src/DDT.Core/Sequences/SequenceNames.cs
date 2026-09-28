// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.RegularExpressions;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Core.Templates;

namespace DDT.Core.Sequences;

// The names a sequence declares, and the checks of what uses them: the declarations, templates, account references and
// shares.
internal sealed partial class SequenceNames
{
    // Kept for DDT's own values, such as the DDT_VAR_ variables of a script's environment.
    private const string ReservedPrefix = "DDT";

    private readonly SequenceDefinition _definition;
    private readonly Dictionary<string, VariableDeclaration> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _valueInputs = new(StringComparer.OrdinalIgnoreCase);
    // Names ignore case everywhere: in templates, conditions, and the account inputs a step names.
    private readonly HashSet<string> _accountInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _valueNames = [];

    public SequenceNames(SequenceDefinition definition)
    {
        _definition = definition;

        foreach (VariableDeclaration? variable in definition.Variables ?? [])
        {
            if (variable?.Name is { Length: > 0 } name)
            {
                _variables.TryAdd(name, variable);
            }
        }

        foreach (InputDeclaration? input in definition.Inputs ?? [])
        {
            if (input?.Name is not { Length: > 0 } name)
            {
                continue;
            }

            if (input.Kind == InputKind.Account)
            {
                _accountInputs.Add(name);
            }
            else
            {
                _valueInputs.Add(name);
            }
        }
    }

    // The names conditions test that only rules and machine roles can give a value, each once, as first written.
    public IReadOnlyList<string> ValueNames => _valueNames;

    public static bool IsName(string name) => Name().IsMatch(name);

    // The type of a fact or run variable of the catalogue, or null for any other name.
    public static FactType? FactTypeOf(string name)
    {
        foreach ((string fact, FactType type) in MachineVariableNames.Catalogue)
        {
            if (string.Equals(fact, name, StringComparison.OrdinalIgnoreCase))
            {
                return type;
            }
        }

        return null;
    }

    public bool IsAccountInput(string name) => _accountInputs.Contains(name);

    public void Tested(string name)
    {
        if (!IsKnown(name) && !IsAccountInput(name) && !_valueNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _valueNames.Add(name);
        }
    }

    // A name a template can use: a fact, a run variable, a declared variable, an input's answer, or one of the values
    // DDT gives every run. An Account input's answer never is one.
    public bool IsKnown(string name) =>
        FactTypeOf(name) is not null
        || _variables.ContainsKey(name)
        || _valueInputs.Contains(name)
        || SequenceValidator.WellKnownNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    public void CheckTemplate(string? text, string field, Action<string?, ServerMessage> add)
    {
        if (text is null)
        {
            return;
        }

        ParsedTemplate parsed = ValueTemplate.Parse(text, name => IsKnown(name) || IsAccountInput(name));

        foreach (string name in parsed.Names.Where(IsAccountInput))
        {
            add(field, ServerMessages.SequenceAccountInputAsValue.With("name", name));
        }

        // An unknown name may be a value a rule or machine role sets, which only the server knows, so it is noted for
        // the server's warning instead of refused. The run fails at the step if nothing sets it.
        foreach (TemplateProblem problem in parsed.Problems)
        {
            if (problem.Kind == TemplateProblemKind.UnknownName)
            {
                Tested(problem.Name);
            }
            else
            {
                add(field, problem.Message());
            }
        }
    }

    // Exactly one of a stored account and an Account input of the sequence. Whether the account exists and may go
    // where the step sends it is the server's to say.
    public void CheckAccount(AccountReference? account, string field, Action<string?, ServerMessage> add)
    {
        bool stored = account?.AccountId is { } id && id != Guid.Empty;
        bool asked = !string.IsNullOrWhiteSpace(account?.Input);

        if (stored == asked)
        {
            add(field, ServerMessages.SequenceAccountChoose.With());
        }
        else if (asked && !_accountInputs.Contains(account!.Input!))
        {
            add($"{field}.input", ServerMessages.SequenceAccountInputUnknown.With("name", account.Input!));
        }
    }

    // A share's host comes only from values fixed when the run starts, so no step can send the account elsewhere.
    public void CheckShares(IReadOnlyList<ShareConnection?>? shares, Action<string?, ServerMessage> add)
    {
        if (shares is null)
        {
            return;
        }

        if (shares.Count > SequenceValidator.MaxShares)
        {
            add("shares", ServerMessages.SequenceTooManyShares.With("max", SequenceValidator.MaxShares));
        }

        for (int index = 0; index < shares.Count; index++)
        {
            string field = string.Create(CultureInfo.InvariantCulture, $"shares[{index}]");

            if (Host(shares[index]?.Path) is not { } host)
            {
                add($"{field}.path", ServerMessages.SequenceSharePath.With());
            }
            else
            {
                CheckTemplate(shares[index]!.Path, $"{field}.path", add);

                foreach (string name in ValueTemplate.Parse(host).Names.Where(ChangesDuringRun))
                {
                    add($"{field}.path", ServerMessages.SequenceShareHostFixed.With("name", name));
                }
            }

            CheckAccount(shares[index]?.Account, $"{field}.account", add);
        }
    }

    public void CheckSetVariable(SetVariableStep step, Action<string?, ServerMessage> add)
    {
        if (string.IsNullOrWhiteSpace(step.Variable))
        {
            add("variable", ServerMessages.SequenceSetVariableChoose.With());
        }
        else if (!_variables.TryGetValue(step.Variable, out VariableDeclaration? declared))
        {
            add("variable", ServerMessages.SequenceVariableNotDeclared.With("name", step.Variable));
        }
        else if (!declared.SetBySteps)
        {
            add("variable", ServerMessages.SequenceVariableNotSetBySteps.With("name", declared.Name));
        }

        CheckTemplate(step.Value, "value", add);
    }

    // The sequence's variables and inputs. An input may set a declared variable, which is how a variable is asked; an
    // Account input sets none, so its name is its own.
    public void CheckDeclarations(List<SequenceProblem> problems)
    {
        void Add(string? field, ServerMessage message) => problems.Add(SequenceProblem.From(null, field, message));

        IReadOnlyList<VariableDeclaration?> variables = _definition.Variables ?? [];
        IReadOnlyList<InputDeclaration?> inputs = _definition.Inputs ?? [];
        HashSet<string> declared = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> asked = new(StringComparer.OrdinalIgnoreCase);

        if (variables.Count > SequenceValidator.MaxVariables)
        {
            Add("variables", ServerMessages.SequenceTooManyVariables.With("max", SequenceValidator.MaxVariables));
        }

        for (int index = 0; index < variables.Count; index++)
        {
            string at = string.Create(CultureInfo.InvariantCulture, $"variables[{index}]");

            if (variables[index] is not { } variable)
            {
                Add(at, ServerMessages.SequenceDeclarationEmpty.With());

                continue;
            }

            if (CheckName(variable.Name, $"{at}.name", Add) && !declared.Add(variable.Name))
            {
                Add($"{at}.name", ServerMessages.SequenceValueNameRepeated.With("name", variable.Name));
            }

            CheckTemplate(variable.Default, $"{at}.default", Add);
        }

        if (inputs.Count > SequenceValidator.MaxInputs)
        {
            Add("inputs", ServerMessages.SequenceTooManyInputs.With("max", SequenceValidator.MaxInputs));
        }

        for (int index = 0; index < inputs.Count; index++)
        {
            string at = string.Create(CultureInfo.InvariantCulture, $"inputs[{index}]");

            if (inputs[index] is not { } input)
            {
                Add(at, ServerMessages.SequenceDeclarationEmpty.With());

                continue;
            }

            if (CheckName(input.Name, $"{at}.name", Add)
                && (!asked.Add(input.Name) || (input.Kind == InputKind.Account && declared.Contains(input.Name))))
            {
                Add($"{at}.name", ServerMessages.SequenceValueNameRepeated.With("name", input.Name));
            }

            CheckInput(input, at, Add);
        }
    }

    private static void CheckInput(InputDeclaration input, string at, Action<string?, ServerMessage> add)
    {
        if (string.IsNullOrWhiteSpace(input.Label) || input.Label.Length > SequenceValidator.MaxNameLength)
        {
            add($"{at}.label", ServerMessages.SequenceInputLabel.With("max", SequenceValidator.MaxNameLength));
        }

        if (!Enum.IsDefined(input.Kind))
        {
            add($"{at}.kind", ServerMessages.SequenceInputKind.With());
        }

        // Every input is asked somewhere, on the web, at the machine or both, so a required one can be answered.
        if (!Enum.IsDefined(input.AskAt))
        {
            add($"{at}.askAt", ServerMessages.SequenceInputAskAt.With());
        }

        if (input.Kind is InputKind.Choice or InputKind.MultiChoice)
        {
            CheckChoices(input, at, add);
        }

        if (input.MaxLength is < 1 or > SequenceValidator.MaxAnswerLength)
        {
            add($"{at}.maxLength", ServerMessages.SequenceInputMaxLength.With("max", SequenceValidator.MaxAnswerLength));
        }
    }

    // A multiple choice's answer is its values separated by semicolons, so no value can hold one.
    private static void CheckChoices(InputDeclaration input, string at, Action<string?, ServerMessage> add)
    {
        IReadOnlyList<InputChoice?> choices = input.Choices ?? [];
        bool several = input.Kind == InputKind.MultiChoice;
        HashSet<string> values = new(StringComparer.OrdinalIgnoreCase);

        if (choices.Count is 0 or > SequenceValidator.MaxChoices)
        {
            add($"{at}.choices", ServerMessages.SequenceInputChoices.With("max", SequenceValidator.MaxChoices));
        }

        for (int index = 0; index < choices.Count; index++)
        {
            string field = string.Create(CultureInfo.InvariantCulture, $"{at}.choices[{index}].value");

            if (choices[index]?.Value is not { } value || string.IsNullOrWhiteSpace(value))
            {
                add(field, ServerMessages.SequenceInputChoiceEmpty.With());
            }
            else if (!values.Add(value))
            {
                add(field, ServerMessages.SequenceInputChoiceRepeated.With("value", value));
            }
            else if (several && value.Contains(';', StringComparison.Ordinal))
            {
                add(field, ServerMessages.SequenceInputChoiceSemicolon.With());
            }
        }

        if (!string.IsNullOrEmpty(input.Default)
            && (several ? input.Default.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [input.Default])
                .Any(part => !values.Contains(part)))
        {
            add($"{at}.default", ServerMessages.SequenceInputDefaultNotChoice.With());
        }
    }

    // A name as templates write it, not DDT's own, and not a fact, which only the machine or the run sets. ComputerName
    // is a fact that is also a value: the machine's name comes from it.
    private static bool CheckName(string? name, string field, Action<string?, ServerMessage> add)
    {
        if (name is null || !IsName(name))
        {
            add(field, ServerMessages.SequenceValueName.With("max", SequenceValidator.MaxValueNameLength));

            return false;
        }

        if (name.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            add(field, ServerMessages.SequenceValueNameReserved.With());

            return false;
        }

        if (FactTypeOf(name) is not null && !string.Equals(name, MachineVariableNames.ComputerName, StringComparison.OrdinalIgnoreCase))
        {
            add(field, ServerMessages.ValuesFact.With("name", name));

            return false;
        }

        return true;
    }

    private bool ChangesDuringRun(string name) =>
        MachineVariableNames.ChangeDuringRun.Contains(name, StringComparer.OrdinalIgnoreCase)
        || (_variables.TryGetValue(name, out VariableDeclaration? variable) && variable.SetBySteps);

    // The host of \\host\share, as it is written, or null when the path is not written so.
    private static string? Host(string? path)
    {
        if (path is null || !path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        int hostEnd = path.IndexOf('\\', 2);

        if (hostEnd < 0)
        {
            return null;
        }

        int shareEnd = path.IndexOf('\\', hostEnd + 1);
        string host = path[2..hostEnd];
        string share = shareEnd < 0 ? path[(hostEnd + 1)..] : path[(hostEnd + 1)..shareEnd];

        return string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(share) ? null : host;
    }

    // As ValueTemplate writes a name, at most MaxValueNameLength characters. \z, because $ would allow a line end.
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Name();
}
