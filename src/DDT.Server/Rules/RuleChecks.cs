// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.RegularExpressions;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Sequences;
using DDT.Core.Templates;

namespace DDT.Server.Rules;

// What is wrong with a rule or a machine role. Bounds refuse a request; problems are saved with a rule and keep it from
// matching, so autosave keeps a draft, while they refuse a role's save. Fields are paths such as "when.parts[0].value".
public static partial class RuleChecks
{
    public const string WhenField = ConditionEvaluator.WhenPath;
    public const string ValuesField = "values";
    public const string RoleIdsField = "roleIds";

    // The run's own variables, which have no value before a run.
    private static readonly string[] s_runVariables = [MachineVariableNames.LastStepFailed, MachineVariableNames.LastExitCode];

    // What a condition's test compares with a value, by the type of what it tests. Every type can be tested for being one
    // of a list, for having a value, and for being equal. A name that is not a fact is a value, which may hold anything.
    private static readonly ConditionOperator[] s_textual =
    [
        ConditionOperator.StartsWith,
        ConditionOperator.EndsWith,
        ConditionOperator.Contains,
        ConditionOperator.NotContains,
        ConditionOperator.Matches,
    ];

    private static readonly ConditionOperator[] s_numeric =
    [
        ConditionOperator.Greater,
        ConditionOperator.GreaterOrEqual,
        ConditionOperator.Less,
        ConditionOperator.LessOrEqual,
    ];

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ValueName();

    // A condition too large to store: more tests than a step may have, or groups nested deeper.
    public static ServerMessage? ConditionBound(ConditionNode? when) =>
        when is not null && (Tests(when) > RuleLimits.MaxTests || Depth(when) > RuleLimits.MaxConditionDepth)
            ? ServerMessages.RuleConditionTooLarge.With("tests", RuleLimits.MaxTests, "depth", RuleLimits.MaxConditionDepth)
            : null;

    // Values too many or too long to store, and a value missing altogether.
    public static ServerMessage? ValuesBound(IReadOnlyList<NamedValue?>? values)
    {
        if (values is null)
        {
            return null;
        }

        if (values.Count > RuleLimits.MaxValues)
        {
            return ServerMessages.NamedValueTooMany.With("max", RuleLimits.MaxValues);
        }

        if (values.Any(value => value is null || value.Name is null || value.Value is null))
        {
            return ServerMessages.NamedValueNameEmpty.With();
        }

        return values.Any(value => value!.Name.Length > RuleLimits.MaxStoredValueNameLength || value.Value.Length > RuleLimits.MaxValueLength)
            ? ServerMessages.NamedValueTooLong.With("name", RuleLimits.MaxValueNameLength, "text", RuleLimits.MaxValueLength)
            : null;
    }

    // A rule's problems. KnownNames are the names rules and machine roles set, which a condition may test besides the
    // facts; KnownRoles the machine roles there are.
    public static IReadOnlyList<SequenceProblem> Problems(
        ConditionNode? when,
        IReadOnlyList<NamedValue> values,
        IReadOnlyList<Guid> roleIds,
        IReadOnlySet<string> knownNames,
        IReadOnlySet<Guid> knownRoles)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(roleIds);
        ArgumentNullException.ThrowIfNull(knownNames);
        ArgumentNullException.ThrowIfNull(knownRoles);

        List<SequenceProblem> problems = [];

        if (when is not null)
        {
            Condition(when, WhenField, knownNames, (field, message) => problems.Add(SequenceProblem.From(null, field, message)));
        }

        problems.AddRange(ValueProblems(values).Select(problem => SequenceProblem.From(null, problem.Field, problem.Message)));

        for (int index = 0; index < roleIds.Count; index++)
        {
            if (!knownRoles.Contains(roleIds[index]))
            {
                problems.Add(SequenceProblem.From(null, Indexed(RoleIdsField, index), ServerMessages.RuleRoleGone.With()));
            }
        }

        return problems;
    }

    // What is wrong with the values a rule or a machine role sets: names a template can use, that are not a fact or DDT's,
    // each once, and templates written as DDT's are.
    public static IReadOnlyList<(string Field, ServerMessage Message)> ValueProblems(IReadOnlyList<NamedValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        List<(string, ServerMessage)> problems = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < values.Count; index++)
        {
            string field = Indexed(ValuesField, index);
            string name = values[index].Name.Trim();

            if (name.Length == 0)
            {
                problems.Add(($"{field}.name", ServerMessages.NamedValueNameEmpty.With()));
            }
            else if (name.Length > RuleLimits.MaxValueNameLength || !ValueName().IsMatch(name))
            {
                problems.Add(($"{field}.name", ServerMessages.NamedValueNameInvalid.With("name", name, "max", RuleLimits.MaxValueNameLength)));
            }
            else if (name.StartsWith("ddt", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(($"{field}.name", ServerMessages.NamedValueReserved.With()));
            }
            else if (IsFact(name))
            {
                problems.Add(($"{field}.name", ServerMessages.ValuesFact.With("name", name)));
            }
            else if (!seen.Add(name))
            {
                problems.Add(($"{field}.name", ServerMessages.NamedValueRepeated.With("name", name)));
            }

            problems.AddRange(ValueTemplate.Parse(values[index].Value).Problems.Select(problem => ($"{field}.value", problem.Message())));
        }

        return problems;
    }

    // A value may set ComputerName, which is where the machine's name comes from; every other fact is the machine's.
    public static bool IsFact(string name) =>
        !string.Equals(name, MachineVariableNames.ComputerName, StringComparison.OrdinalIgnoreCase) && MachineVariables.Fact(name) is not null
        || s_runVariables.Contains(name, StringComparer.OrdinalIgnoreCase);

    // Every name a condition tests, for knowing whether it needs the values of the rules above it.
    public static IEnumerable<string> Names(ConditionNode? node) => node switch
    {
        TestCondition { Variable: { Length: > 0 } variable } => [variable],
        ConditionGroup group => ((IReadOnlyList<ConditionNode?>?)group.Parts ?? []).SelectMany(Names),
        _ => [],
    };

    private static void Condition(ConditionNode? node, string path, IReadOnlySet<string> knownNames, Action<string, ServerMessage> add)
    {
        switch (node)
        {
            case TestCondition test:
                Test(test, path, knownNames, add);
                break;

            case ConditionGroup group:
                IReadOnlyList<ConditionNode?> parts = group.Parts ?? [];

                for (int index = 0; index < parts.Count; index++)
                {
                    Condition(parts[index], $"{path}.{Indexed("parts", index)}", knownNames, add);
                }

                break;

            default:
                add(path, ServerMessages.SequenceConditionEmpty.With());
                break;
        }
    }

    private static void Test(TestCondition test, string path, IReadOnlySet<string> knownNames, Action<string, ServerMessage> add)
    {
        // Not trimmed: the evaluator reads the name as it is written.
        string variable = test.Variable ?? "";
        string? fact = MachineVariables.Fact(variable);

        if (NameProblem(variable, fact, knownNames) is { } name)
        {
            add($"{path}.variable", name);

            return;
        }

        if (!Enum.IsDefined(test.Operator))
        {
            add($"{path}.operator", ServerMessages.SequenceConditionOperator.With());

            return;
        }

        FactType type = fact is null ? FactType.Text : ConditionEvaluator.TypeOf(fact);

        if (!Fits(test.Operator, type, fact is not null))
        {
            add($"{path}.operator", ServerMessages.RuleConditionOperatorType.With("name", fact ?? variable, "type", TypeName(type)));

            return;
        }

        if (test.Operator is ConditionOperator.Exists or ConditionOperator.NotExists)
        {
            return;
        }

        string value = test.Value?.Trim() ?? "";

        if (value.Length == 0)
        {
            add($"{path}.value", ServerMessages.SequenceConditionValue.With());

            return;
        }

        if (ValueProblem(test.Operator, fact is null ? null : type, value) is { } problem)
        {
            add($"{path}.value", problem);
        }
    }

    private static ServerMessage? NameProblem(string variable, string? fact, IReadOnlySet<string> knownNames)
    {
        if (string.IsNullOrWhiteSpace(variable))
        {
            return ServerMessages.RuleConditionChooseName.With();
        }

        if (s_runVariables.Contains(variable, StringComparer.OrdinalIgnoreCase))
        {
            return ServerMessages.RuleConditionRunVariable.With("name", variable);
        }

        return fact is null && !knownNames.Contains(variable) ? ServerMessages.RuleConditionUnknownName.With("name", variable) : null;
    }

    private static bool Fits(ConditionOperator comparison, FactType type, bool fact)
    {
        if (!fact || comparison is ConditionOperator.Equals or ConditionOperator.NotEquals or ConditionOperator.In
            or ConditionOperator.Exists or ConditionOperator.NotExists)
        {
            return true;
        }

        if (s_textual.Contains(comparison))
        {
            return type is FactType.Text or FactType.Mac or FactType.IPv4;
        }

        if (s_numeric.Contains(comparison))
        {
            return type == FactType.Number;
        }

        return comparison == ConditionOperator.InSubnet && type == FactType.IPv4;
    }

    // The value in the form the comparison needs. Type is null for a value's name, which is typed only where the
    // comparison says, as a number or a network.
    private static ServerMessage? ValueProblem(ConditionOperator comparison, FactType? type, string value)
    {
        if (comparison == ConditionOperator.InSubnet)
        {
            return Ipv4.TryParseNetwork(value, out _, out _) ? null : ServerMessages.RuleConditionSubnet.With();
        }

        if (s_numeric.Contains(comparison))
        {
            return Number(value) ? null : ServerMessages.RuleConditionNumber.With();
        }

        string[] items = comparison == ConditionOperator.In
            ? value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : [value];
        bool whole = comparison is ConditionOperator.Equals or ConditionOperator.NotEquals or ConditionOperator.In;

        return type switch
        {
            FactType.Mac when comparison != ConditionOperator.Matches && !items.All(item => Mac(item, whole)) => whole
                ? ServerMessages.MacEnterFull.With()
                : ServerMessages.MacEnterPart.With(),
            FactType.Number when whole && !items.All(Number) => ServerMessages.RuleConditionNumber.With(),
            FactType.YesNo when whole && !items.All(YesNo) => ServerMessages.RuleConditionYesNo.With(),
            FactType.IPv4 when whole && !items.All(item => Ipv4.TryParse(item, out _)) => ServerMessages.RuleConditionAddress.With(),
            _ => null,
        };
    }

    // Twelve hex digits for a whole address, and one to twelve for a part of one, with or without separators.
    private static bool Mac(string value, bool whole)
    {
        string digits = string.Concat(value.Where(c => c is not (':' or '-' or '.' or ' ')));

        return (whole ? digits.Length == 12 : digits.Length is > 0 and <= 12) && digits.All(char.IsAsciiHexDigit);
    }

    private static bool Number(string value) => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static bool YesNo(string value) => value.ToUpperInvariant() is "TRUE" or "YES" or "1" or "FALSE" or "NO" or "0";

    private static string TypeName(FactType type) => type switch
    {
        FactType.Number => "number",
        FactType.YesNo => "yesNo",
        FactType.IPv4 => "ipv4",
        FactType.Mac => "mac",
        _ => "text",
    };

    private static int Tests(ConditionNode? node) => node switch
    {
        TestCondition => 1,
        ConditionGroup group => (group.Parts ?? []).Sum(Tests),
        _ => 0,
    };

    // How deep groups are nested: a test alone is 0, a group of tests 1.
    private static int Depth(ConditionNode? node) => node switch
    {
        ConditionGroup group => 1 + (group.Parts ?? []).Select(Depth).DefaultIfEmpty(0).Max(),
        _ => 0,
    };

    private static string Indexed(string field, int index) => string.Create(CultureInfo.InvariantCulture, $"{field}[{index}]");
}
