// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// Warns of a condition that compares Phase with a phase the sequence's kind never runs in.
internal static class PhaseConditions
{
    public static void Check(SequenceStep step, SequenceKind kind, Action<string?, ServerMessage> warn)
    {
        IReadOnlyList<StepCondition?> conditions = step.Conditions ?? [];

        for (int index = 0; index < conditions.Count; index++)
        {
            if (conditions[index] is { } condition)
            {
                string path = string.Create(CultureInfo.InvariantCulture, $"{ConditionEvaluator.ConditionsPath}[{index}]");
                Test(condition.Variable, condition.Operator, condition.Value, new Place(path, kind, warn));
            }
        }

        Walk(step.When, new Place(ConditionEvaluator.WhenPath, kind, warn));
        Walk((step as IfStep)?.Test, new Place(ConditionEvaluator.TestPath, kind, warn));
        Walk((step as RepeatStep)?.Until, new Place(ConditionEvaluator.UntilPath, kind, warn));
    }

    // ConditionChecks reports a tree that is too deep, and such a tree is short of a document's size limit anyway.
    private static void Walk(ConditionNode? node, Place place)
    {
        if (node is TestCondition test)
        {
            Test(test.Variable, test.Operator, test.Value, place);
        }
        else if (node is ConditionGroup { Parts: { } parts })
        {
            for (int index = 0; index < parts.Count; index++)
            {
                Walk(parts[index], place with { Path = string.Create(CultureInfo.InvariantCulture, $"{place.Path}.parts[{index}]") });
            }
        }
    }

    private static void Test(string? variable, ConditionOperator op, string? value, Place place)
    {
        if (!string.Equals(variable, MachineVariableNames.Phase, StringComparison.OrdinalIgnoreCase)
            || op is not (ConditionOperator.Equals or ConditionOperator.NotEquals or ConditionOperator.In))
        {
            return;
        }

        string[] items = op == ConditionOperator.In
            ? (value ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : [(value ?? "").Trim()];

        // Comparisons ignore case, so the names do too.
        SequencePhase[] other =
        [
            .. Enum.GetValues<SequencePhase>().Where(phase =>
                SequenceKinds.Of(phase) != place.Kind && items.Contains(phase.ToString(), StringComparer.OrdinalIgnoreCase)),
        ];

        if (other is [var first, ..])
        {
            place.Warn(
                $"{place.Path}.value",
                ServerMessages.SequencePhaseValueOtherKindWarning.With("kind", place.Kind.ToString(), "value", first.ToString()));
        }
    }

    private sealed record Place(string Path, SequenceKind Kind, Action<string?, ServerMessage> Warn);
}
