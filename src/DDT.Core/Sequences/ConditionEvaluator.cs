// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// Comparisons ignore case. A variable with several values, such as MacAddress, meets Equals, StartsWith and Contains
// when any value does, and NotEquals when no value equals. A value the machine did not report meets only NotEquals.
public static class ConditionEvaluator
{
    public static bool Holds(IReadOnlyList<StepCondition> conditions, MachineVariables machine)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(machine);

        return conditions.All(condition => Holds(condition, machine));
    }

    public static bool Holds(StepCondition condition, MachineVariables machine)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(machine);

        // The validator refuses an unknown variable; one that slips through never lets a step run.
        if (!MachineVariableNames.All.Contains(condition.Variable, StringComparer.Ordinal))
        {
            return false;
        }

        bool mac = condition.Variable == MachineVariableNames.MacAddress;
        string expected = mac ? NormaliseMac(condition.Value) : condition.Value;
        IEnumerable<string> values = machine.Values(condition.Variable).Select(value => mac ? NormaliseMac(value) : value);

        return condition.Operator switch
        {
            ConditionOperator.Equals => values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)),
            ConditionOperator.NotEquals => !values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)),
            ConditionOperator.StartsWith => values.Any(value => value.StartsWith(expected, StringComparison.OrdinalIgnoreCase)),
            ConditionOperator.Contains when mac => values.Any(value => ContainsAtByte(value, expected)),
            ConditionOperator.Contains => values.Any(value => value.Contains(expected, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
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

    // 00:15:5d:01:02:03, 00-15-5D-01-02-03 and 0015.5d01.0203 all become 00155D010203; a prefix such as 00:15:5D
    // becomes 00155D.
    private static string NormaliseMac(string value) =>
        string.Concat(value.Where(c => c is not (':' or '-' or '.' or ' '))).ToUpperInvariant();
}
