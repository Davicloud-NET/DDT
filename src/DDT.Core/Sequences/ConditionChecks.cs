// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// The conditions of a node: the legacy list, which agents of versions 1 and 2 run, and the trees of version 3, a node's
// When, an IF's Test and a repeat's Until. A problem's field goes into the tree, such as when.parts[1].value, as
// ConditionEvaluator names a test's path.
internal static class ConditionChecks
{
    // The operators that fit a fact of each type. Rules, machine roles and the sequence give values of no type the
    // validator knows, so every operator fits them.
    private static readonly ConditionOperator[] s_text =
    [
        ConditionOperator.Equals, ConditionOperator.NotEquals, ConditionOperator.StartsWith, ConditionOperator.EndsWith,
        ConditionOperator.Contains, ConditionOperator.NotContains, ConditionOperator.Matches, ConditionOperator.In,
        ConditionOperator.Exists, ConditionOperator.NotExists,
    ];

    private static readonly ConditionOperator[] s_number =
    [
        ConditionOperator.Equals, ConditionOperator.NotEquals, ConditionOperator.In, ConditionOperator.Exists,
        ConditionOperator.NotExists, ConditionOperator.Greater, ConditionOperator.GreaterOrEqual, ConditionOperator.Less,
        ConditionOperator.LessOrEqual,
    ];

    private static readonly ConditionOperator[] s_yesNo =
    [
        ConditionOperator.Equals, ConditionOperator.NotEquals, ConditionOperator.Exists, ConditionOperator.NotExists,
    ];

    private static readonly ConditionOperator[] s_ipv4 =
    [
        ConditionOperator.Equals, ConditionOperator.NotEquals, ConditionOperator.StartsWith, ConditionOperator.Matches,
        ConditionOperator.In, ConditionOperator.Exists, ConditionOperator.NotExists, ConditionOperator.InSubnet,
    ];

    private static readonly ConditionOperator[] s_mac =
    [
        ConditionOperator.Equals, ConditionOperator.NotEquals, ConditionOperator.StartsWith, ConditionOperator.EndsWith,
        ConditionOperator.Contains, ConditionOperator.NotContains, ConditionOperator.In, ConditionOperator.Exists,
        ConditionOperator.NotExists,
    ];

    // Checks the legacy list and returns how many conditions it has. Their evaluator knows only the variables and
    // operators of version 1, and holds nothing else, so the list takes nothing else.
    public static int Legacy(IReadOnlyList<StepCondition?>? conditions, Action<string?, ServerMessage> add)
    {
        if (conditions is null)
        {
            add(ConditionEvaluator.ConditionsPath, ServerMessages.SequenceConditionsMissing.With());

            return 0;
        }

        if (conditions.Count > SequenceValidator.MaxConditions)
        {
            add(ConditionEvaluator.ConditionsPath, ServerMessages.SequenceTooManyConditions.With("max", SequenceValidator.MaxConditions));
        }

        for (int index = 0; index < conditions.Count; index++)
        {
            string field = string.Create(CultureInfo.InvariantCulture, $"{ConditionEvaluator.ConditionsPath}[{index}]");

            if (conditions[index] is not { } condition)
            {
                add(field, ServerMessages.SequenceConditionEmpty.With());

                continue;
            }

            if (!MachineVariableNames.All.Contains(condition.Variable, StringComparer.Ordinal))
            {
                add($"{field}.variable", ServerMessages.SequenceConditionVariable.With("variables", string.Join(", ", MachineVariableNames.All)));
            }

            if (!Enum.IsDefined(condition.Operator) || condition.Operator > ConditionOperator.Contains)
            {
                add($"{field}.operator", ServerMessages.SequenceConditionOperator.With());
            }

            if (string.IsNullOrWhiteSpace(condition.Value))
            {
                add($"{field}.value", ServerMessages.SequenceConditionValue.With());
            }
            else if (condition.Variable == MachineVariableNames.MacAddress)
            {
                Mac(condition.Operator, [condition.Value], $"{field}.value", add);
            }
        }

        return conditions.Count;
    }

    // Checks a tree at path, such as "when", and returns how many tests it has.
    public static int Tree(ConditionNode condition, string path, SequenceNames names, Action<string?, ServerMessage> add) =>
        Walk(condition, path, 0, names, add);

    private static int Walk(ConditionNode? node, string path, int groups, SequenceNames names, Action<string?, ServerMessage> add)
    {
        switch (node)
        {
            case TestCondition test:
                Test(test, path, names, add);

                return 1;

            case ConditionGroup group:
                if (groups == SequenceValidator.MaxConditionDepth)
                {
                    add(path, ServerMessages.SequenceConditionTooDeep.With("max", SequenceValidator.MaxConditionDepth));

                    return 0;
                }

                IReadOnlyList<ConditionNode?> parts = group.Parts ?? [];
                int tests = 0;

                for (int index = 0; index < parts.Count; index++)
                {
                    tests += Walk(parts[index], string.Create(CultureInfo.InvariantCulture, $"{path}.parts[{index}]"), groups + 1, names, add);
                }

                return tests;

            default:
                add(path, ServerMessages.SequenceConditionEmpty.With());

                return 0;
        }
    }

    private static void Test(TestCondition test, string path, SequenceNames names, Action<string?, ServerMessage> add)
    {
        string? variable = test.Variable;
        bool named = variable is not null && SequenceNames.IsName(variable);

        if (!named)
        {
            add($"{path}.variable", ServerMessages.SequenceConditionName.With());
        }
        else if (names.IsAccountInput(variable!))
        {
            add($"{path}.variable", ServerMessages.SequenceAccountInputAsValue.With("name", variable!));
        }
        else
        {
            names.Tested(variable!);
        }

        if (!Enum.IsDefined(test.Operator))
        {
            add($"{path}.operator", ServerMessages.SequenceConditionOperator.With());

            return;
        }

        FactType? type = named ? SequenceNames.FactTypeOf(variable!) : null;

        if (type is { } typed && !Fits(typed).Contains(test.Operator))
        {
            add($"{path}.operator", ServerMessages.SequenceOperatorDoesNotFit.With("name", variable!, "type", typed.ToString()));

            return;
        }

        if (test.Operator is ConditionOperator.Exists or ConditionOperator.NotExists)
        {
            return;
        }

        bool list = test.Operator == ConditionOperator.In;
        string[] items = list
            ? (test.Value ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : [(test.Value ?? "").Trim()];

        if (items is [] or [""])
        {
            add($"{path}.value", ServerMessages.SequenceConditionValue.With());
        }
        else if (type is { } fact)
        {
            Value(fact, test.Operator, items, $"{path}.value", add);
        }
    }

    private static ConditionOperator[] Fits(FactType type) => type switch
    {
        FactType.Number => s_number,
        FactType.YesNo => s_yesNo,
        FactType.IPv4 => s_ipv4,
        FactType.Mac => s_mac,
        _ => s_text,
    };

    // A value of the fact's type, so a test cannot quietly never hold. Text and patterns take anything.
    private static void Value(FactType type, ConditionOperator op, string[] items, string field, Action<string?, ServerMessage> add)
    {
        string list = op == ConditionOperator.In ? "yes" : "no";

        switch (type)
        {
            case FactType.Number when !items.All(item => decimal.TryParse(item, NumberStyles.Float, CultureInfo.InvariantCulture, out _)):
                add(field, ServerMessages.SequenceConditionNumber.With("list", list));
                break;
            case FactType.YesNo when !items.All(IsYesNo):
                add(field, ServerMessages.SequenceConditionYesNo.With());
                break;
            case FactType.IPv4 when op == ConditionOperator.InSubnet:
                if (!Ipv4.TryParseNetwork(items[0], out _, out _))
                {
                    add(field, ServerMessages.SequenceConditionSubnet.With());
                }

                break;
            case FactType.IPv4 when op is ConditionOperator.Equals or ConditionOperator.NotEquals or ConditionOperator.In:
                if (!items.All(item => Ipv4.TryParse(item, out _)))
                {
                    add(field, ServerMessages.SequenceConditionIPv4.With("list", list));
                }

                break;
            case FactType.Mac:
                Mac(op, items, field, add);
                break;
        }
    }

    private static bool IsYesNo(string value) =>
        value.ToUpperInvariant() is "TRUE" or "FALSE" or "YES" or "NO" or "1" or "0";

    // Separators alone would leave nothing to compare, and StartsWith or Contains would then hold on every machine. A
    // whole address is compared whole, so it needs all 12 digits.
    private static void Mac(ConditionOperator op, string[] items, string field, Action<string?, ServerMessage> add)
    {
        bool whole = op is ConditionOperator.Equals or ConditionOperator.NotEquals or ConditionOperator.In;

        foreach (string item in items)
        {
            string digits = ConditionEvaluator.NormaliseMac(item);
            bool hex = digits.All(char.IsAsciiHexDigit);

            if (whole ? digits.Length != 12 || !hex : digits.Length is 0 or > 12 || !hex)
            {
                add(field, (whole ? ServerMessages.MacEnterFull : ServerMessages.MacEnterPart).With());

                return;
            }
        }
    }
}
