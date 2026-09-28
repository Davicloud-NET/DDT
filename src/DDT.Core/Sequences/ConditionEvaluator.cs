// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Sequences;
using DDT.Core.Machines;

namespace DDT.Core.Sequences;

// Comparisons ignore case. In a tree a positive operator holds when any of a name's values meets it, and NotEquals,
// NotContains and NotExists when none does; legacy Conditions keep the rules of versions 1 and 2. A name is tested by
// its type in MachineVariableNames.Catalogue, else as text, and model names as HardwareModels cleans them.
public static class ConditionEvaluator
{
    // Where a node's condition is, as a SequenceProblem's Field and a TestEvaluation's Path name it.
    public const string ConditionsPath = "conditions";
    public const string WhenPath = "when";
    public const string TestPath = "test";
    public const string UntilPath = "until";

    public static bool Holds(IReadOnlyList<StepCondition> conditions, MachineVariables machine)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(machine);

        return conditions.All(condition => Holds(condition, machine));
    }

    public static bool Holds(StepCondition condition, MachineVariables machine) => Legacy(condition, machine, out _);

    // Null holds, as a step without conditions runs.
    public static bool Holds(ConditionNode? condition, MachineVariables machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return condition is null || Evaluate(condition, machine, "", null);
    }

    // A step's own conditions: every one of its Conditions, and its When. An IF's Test and a repeat's Until are not the
    // step's own; evaluate them with TestPath and UntilPath.
    public static ConditionResult Evaluate(SequenceStep step, MachineVariables machine)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(machine);

        List<TestEvaluation> evaluations = [];
        IReadOnlyList<StepCondition?> conditions = step.Conditions ?? [];
        bool held = true;

        for (int index = 0; index < conditions.Count; index++)
        {
            // The validator refuses an empty condition; one that slips through never lets a step run.
            if (conditions[index] is not { } condition)
            {
                held = false;
                continue;
            }

            bool one = Legacy(condition, machine, out string? actual);
            held &= one;
            Record(evaluations, string.Create(CultureInfo.InvariantCulture, $"{ConditionsPath}[{index}]"), one, actual);
        }

        if (step.When is { } when)
        {
            held &= Evaluate(when, machine, WhenPath, evaluations);
        }

        return new ConditionResult(held, evaluations);
    }

    // Null holds and records nothing. Path is where the condition is within its step or rule, such as TestPath.
    public static ConditionResult Evaluate(ConditionNode? condition, MachineVariables machine, string path)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(path);

        List<TestEvaluation> evaluations = [];

        return new ConditionResult(condition is null || Evaluate(condition, machine, path, evaluations), evaluations);
    }

    // What a name holds: the type the catalogue gives a fact or a run variable, and Text for every other name.
    public static FactType TypeOf(string variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        foreach ((string name, FactType type) in MachineVariableNames.Catalogue)
        {
            if (string.Equals(name, variable, StringComparison.OrdinalIgnoreCase))
            {
                return type;
            }
        }

        return FactType.Text;
    }

    // 00:15:5d:01:02:03, 00-15-5D-01-02-03 and 0015.5d01.0203 all become 00155D010203; a prefix such as 00:15:5D
    // becomes 00155D.
    internal static string NormaliseMac(string value) =>
        string.Concat(value.Where(c => c is not (':' or '-' or '.' or ' '))).ToUpperInvariant();

    // Every part is evaluated, so the run can show each test, however the group turned out. A part that is null, or of
    // a kind this version does not know, does not hold; the validator refuses both.
    private static bool Evaluate(ConditionNode? node, MachineVariables machine, string path, List<TestEvaluation>? evaluations)
    {
        switch (node)
        {
            case TestCondition test:
                bool held = Test(test, machine, out string? actual);

                if (evaluations is not null)
                {
                    Record(evaluations, path, held, actual);
                }

                return held;

            case ConditionGroup group:
                IReadOnlyList<ConditionNode?> parts = group.Parts ?? [];
                int holding = 0;

                for (int index = 0; index < parts.Count; index++)
                {
                    string part = string.Create(CultureInfo.InvariantCulture, $"{path}{(path.Length > 0 ? "." : "")}parts[{index}]");

                    if (Evaluate(parts[index], machine, part, evaluations))
                    {
                        holding++;
                    }
                }

                return group switch
                {
                    AllCondition => holding == parts.Count,
                    AnyCondition => holding > 0,
                    NoneCondition => holding == 0,
                    _ => false,
                };

            default:
                return false;
        }
    }

    private static bool Legacy(StepCondition condition, MachineVariables machine, out string? actual)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(machine);

        actual = null;

        // The validator refuses an unknown variable; one that slips through never lets a step run.
        if (!MachineVariableNames.All.Contains(condition.Variable, StringComparer.Ordinal))
        {
            return false;
        }

        bool mac = condition.Variable == MachineVariableNames.MacAddress;
        string expected = mac ? NormaliseMac(condition.Value) : condition.Value;
        IReadOnlyList<string> reported = machine.Values(condition.Variable);
        string[] values = [.. reported.Select(value => mac ? NormaliseMac(value) : value)];

        bool Equal(string value) => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        bool Starts(string value) => value.StartsWith(expected, StringComparison.OrdinalIgnoreCase);
        bool Has(string value) => mac ? ContainsAtByte(value, expected) : value.Contains(expected, StringComparison.OrdinalIgnoreCase);

        return condition.Operator switch
        {
            ConditionOperator.Equals => Any(reported, values, Equal, out actual),
            ConditionOperator.NotEquals => None(reported, values, Equal, out actual),
            ConditionOperator.StartsWith => Any(reported, values, Starts, out actual),
            ConditionOperator.Contains => Any(reported, values, Has, out actual),
            _ => Never(reported, out actual),
        };
    }

    private static bool Test(TestCondition test, MachineVariables machine, out string? actual)
    {
        actual = null;

        if (string.IsNullOrEmpty(test.Variable))
        {
            return false;
        }

        FactType type = TypeOf(test.Variable);
        bool model = MachineVariables.Fact(test.Variable)
            is MachineVariableNames.Manufacturer or MachineVariableNames.Model or MachineVariableNames.FriendlyModel;
        List<string> reported = [];
        List<string> values = [];

        foreach (string value in machine.Values(test.Variable))
        {
            if (Comparable(value, type, model) is { } comparable)
            {
                reported.Add(value);
                values.Add(comparable);
            }
        }

        string written = test.Value ?? "";
        string expected = Comparable(written, type, model) ?? "";
        (uint Network, uint Mask)? subnet = Ipv4.TryParseNetwork(written, out uint network, out uint mask) ? (network, mask) : null;
        List<string> items =
        [
            .. written.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(item => Comparable(item, type, model))
                .OfType<string>(),
        ];

        return test.Operator switch
        {
            ConditionOperator.Equals => Any(reported, values, value => Same(value, expected, type), out actual),
            ConditionOperator.NotEquals => None(reported, values, value => Same(value, expected, type), out actual),
            ConditionOperator.StartsWith => Any(reported, values, value => StartsWith(value, expected), out actual),
            ConditionOperator.EndsWith => Any(reported, values, value => EndsWith(value, expected, type), out actual),
            ConditionOperator.Contains => Any(reported, values, value => Contains(value, expected, type), out actual),
            ConditionOperator.NotContains => None(reported, values, value => Contains(value, expected, type), out actual),
            ConditionOperator.Matches => Any(reported, values, value => Glob(value, expected), out actual),
            ConditionOperator.In => Any(reported, values, value => items.Any(item => Same(value, item, type)), out actual),
            ConditionOperator.Exists => !None(reported, values, _ => true, out actual),
            ConditionOperator.NotExists => None(reported, values, _ => true, out actual),
            ConditionOperator.Greater => Any(reported, values, value => Compare(value, expected) > 0, out actual),
            ConditionOperator.GreaterOrEqual => Any(reported, values, value => Compare(value, expected) >= 0, out actual),
            ConditionOperator.Less => Any(reported, values, value => Compare(value, expected) < 0, out actual),
            ConditionOperator.LessOrEqual => Any(reported, values, value => Compare(value, expected) <= 0, out actual),
            ConditionOperator.InSubnet => Any(reported, values, value => subnet is { } net && Within(value, net), out actual),

            // An operator this version does not know never holds.
            _ => Never(reported, out actual),
        };
    }

    // What a value is compared as: cleaned and in upper case for the model names, with a board maker's placeholder as
    // no value; a MAC address's hex digits alone; anything else as it is.
    private static string? Comparable(string value, FactType type, bool model)
    {
        if (model)
        {
            return HardwareModels.IsPlaceholder(value) ? null : HardwareModels.Normalize(value);
        }

        return type == FactType.Mac ? NormaliseMac(value) : value;
    }

    // Numbers, yes or no and IPv4 addresses are the same when their values are, so 2.0 is 2 and yes is true; a value
    // that is not of its type is compared as text.
    private static bool Same(string value, string expected, FactType type)
    {
        switch (type)
        {
            case FactType.Number when Number(value) is { } number && Number(expected) is { } other:
                return number == other;
            case FactType.YesNo when YesNo(value) is { } yes && YesNo(expected) is { } other:
                return yes == other;
            case FactType.IPv4 when Ipv4.TryParse(value, out uint address) && Ipv4.TryParse(expected, out uint other):
                return address == other;
            default:
                return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool StartsWith(string value, string expected) => value.StartsWith(expected, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string value, string expected, FactType type) =>
        type == FactType.Mac ? ContainsAtByte(value, expected) : value.Contains(expected, StringComparison.OrdinalIgnoreCase);

    // A MAC address ends with a part only where a byte starts, as for Contains.
    private static bool EndsWith(string value, string expected, FactType type) =>
        value.EndsWith(expected, StringComparison.OrdinalIgnoreCase) && (type != FactType.Mac || (value.Length - expected.Length) % 2 == 0);

    // Null, which no comparison meets, when either is not a number.
    private static int? Compare(string value, string expected) =>
        Number(value) is { } number && Number(expected) is { } other ? number.CompareTo(other) : null;

    private static decimal? Number(string value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number) ? number : null;

    private static bool? YesNo(string value) => value.Trim().ToUpperInvariant() switch
    {
        "TRUE" or "YES" or "1" => true,
        "FALSE" or "NO" or "0" => false,
        _ => null,
    };

    private static bool Within(string value, (uint Network, uint Mask) subnet) =>
        Ipv4.TryParse(value, out uint address) && (address & subnet.Mask) == subnet.Network;

    // * stands for any text and ? for one character, ignoring case. Going back to the last * is enough for a pattern
    // without character classes.
    private static bool Glob(string value, string pattern)
    {
        int at = 0;
        int next = 0;
        int star = -1;
        int resume = 0;

        while (at < value.Length)
        {
            if (next < pattern.Length
                && (pattern[next] == '?' || char.ToUpperInvariant(pattern[next]) == char.ToUpperInvariant(value[at])))
            {
                at++;
                next++;
            }
            else if (next < pattern.Length && pattern[next] == '*')
            {
                star = next++;
                resume = at;
            }
            else if (star >= 0)
            {
                next = star + 1;
                at = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (next < pattern.Length && pattern[next] == '*')
        {
            next++;
        }

        return next == pattern.Length;
    }

    // A0:B0 must not match 0A:0B:0C by taking half of two bytes, so a part of a MAC matches only where a byte starts.
    private static bool ContainsAtByte(string mac, string part)
    {
        for (int index = 0; index < mac.Length; index += 2)
        {
            if (mac.AsSpan(index).StartsWith(part, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // A positive test holds when any value meets it, and shows that value; one that does not hold shows them all.
    private static bool Any(IReadOnlyList<string> reported, IReadOnlyList<string> values, Func<string, bool> meets, out string? actual)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (meets(values[index]))
            {
                actual = reported[index];

                return true;
            }
        }

        actual = Shown(reported);

        return false;
    }

    // A negative test holds when no value meets its positive, and shows every value.
    private static bool None(IReadOnlyList<string> reported, IReadOnlyList<string> values, Func<string, bool> meets, out string? actual)
    {
        actual = Shown(reported);

        return !values.Any(meets);
    }

    private static bool Never(IReadOnlyList<string> reported, out string? actual)
    {
        actual = Shown(reported);

        return false;
    }

    private static string? Shown(IReadOnlyList<string> reported) => reported.Count == 0 ? null : string.Join(", ", reported);

    private static void Record(List<TestEvaluation> evaluations, string path, bool held, string? actual)
    {
        if (evaluations.Count < TestEvaluation.MaxPerNode)
        {
            evaluations.Add(new TestEvaluation(
                path,
                held,
                actual is { Length: > TestEvaluation.MaxActualLength } ? actual[..TestEvaluation.MaxActualLength] : actual));
        }
    }
}
