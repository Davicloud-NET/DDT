// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Says in the log's words why a node of a tree went the way it did, from the tests the engine kept in its Evaluation:
// the run's values and the variables steps set are gone by the time the log reads them, so nothing is tested again. A
// test is found by the path the engine gave it. Only the tests that decided a condition are named: those that failed an
// all, the one that held an any, and the like.
internal static class ConditionStory
{
    private static readonly Dictionary<string, string> s_labels = new(StringComparer.Ordinal)
    {
        [MachineVariableNames.Manufacturer] = "Manufacturer",
        [MachineVariableNames.Model] = "Model",
        [MachineVariableNames.SerialNumber] = "Serial number",
        [MachineVariableNames.SmbiosUuid] = "SMBIOS UUID",
        [MachineVariableNames.MacAddress] = "MAC address",
        [MachineVariableNames.ComputerName] = "Computer name",
        [MachineVariableNames.Phase] = "Phase",
    };

    // The tests of the node's own conditions that did not hold, which is why it was skipped.
    public static IReadOnlyList<string> Unmet(SequenceStep node, IReadOnlyList<TestEvaluation> evaluations)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(evaluations);

        Dictionary<string, TestEvaluation> byPath = ByPath(evaluations);
        List<string> unmet = [];
        IReadOnlyList<StepCondition?> conditions = node.Conditions ?? [];

        for (int index = 0; index < conditions.Count; index++)
        {
            string path = string.Create(CultureInfo.InvariantCulture, $"{ConditionEvaluator.ConditionsPath}[{index}]");

            if (conditions[index] is { } condition && byPath.TryGetValue(path, out TestEvaluation? test) && !test.Held)
            {
                unmet.Add(Describe(condition.Variable, condition.Operator, condition.Value, test.Actual));
            }
        }

        if (Decide(node.When, ConditionEvaluator.WhenPath, byPath) is { Held: false } when)
        {
            unmet.AddRange(when.Tests);
        }

        return unmet;
    }

    // Whether the condition held as the tests at path say, and the tests that decided it; Held is null when a test it
    // needed was not kept, as happens past TestEvaluation.MaxPerNode.
    public static (bool? Held, IReadOnlyList<string> Tests) Decided(ConditionNode? condition, string path, IReadOnlyList<TestEvaluation> evaluations)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(evaluations);

        return Decide(condition, path, ByPath(evaluations)) is { } decided ? (decided.Held, decided.Tests) : (null, []);
    }

    // "Model starts with "OptiPlex", and the machine reports "Latitude 5440"", as the web says it. actual is what the test
    // was checked against, left out when there was none.
    public static string Describe(string variable, ConditionOperator op, string? value, string? actual)
    {
        ArgumentNullException.ThrowIfNull(variable);

        string label = s_labels.GetValueOrDefault(variable, variable);
        string described = op switch
        {
            ConditionOperator.Exists => $"{label} has a value",
            ConditionOperator.NotExists => $"{label} has no value",
            _ => $"{label} {Words(op)} \"{value}\"",
        };

        if (actual is null)
        {
            return described;
        }

        string whose = MachineVariables.Fact(variable) is null ? "the run's value is" : "the machine reports";

        return $"{described}, and {whose} \"{Shown(variable, actual)}\"";
    }

    // One sentence's end for a list of tests: "this condition did not hold: x." or "these conditions did not hold: x; y."
    public static string Sentence(IReadOnlyList<string> tests, string one, string several, string none) => tests.Count switch
    {
        0 => none,
        1 => $"{one}: {tests[0]}.",
        _ => $"{several}: {string.Join("; ", tests)}.",
    };

    private static Dictionary<string, TestEvaluation> ByPath(IReadOnlyList<TestEvaluation> evaluations)
    {
        Dictionary<string, TestEvaluation> byPath = new(StringComparer.Ordinal);

        foreach (TestEvaluation evaluation in evaluations)
        {
            byPath.TryAdd(evaluation.Path, evaluation);
        }

        return byPath;
    }

    // Null when the condition is not there or a test it needed was not kept. The paths are the evaluator's: a group's
    // parts are path.parts[i].
    private static Decision? Decide(ConditionNode? node, string path, Dictionary<string, TestEvaluation> evaluations)
    {
        switch (node)
        {
            case TestCondition test when evaluations.TryGetValue(path, out TestEvaluation? evaluation):
                return new Decision(evaluation.Held, [Describe(test.Variable ?? "", test.Operator, test.Value, evaluation.Actual)]);

            case ConditionGroup group:
                IReadOnlyList<ConditionNode?> parts = group.Parts ?? [];
                List<Decision> decided = [];

                for (int index = 0; index < parts.Count; index++)
                {
                    if (Decide(parts[index], string.Create(CultureInfo.InvariantCulture, $"{path}.parts[{index}]"), evaluations) is not { } part)
                    {
                        return null;
                    }

                    decided.Add(part);
                }

                int holding = decided.Count(part => part.Held);
                bool held = group switch
                {
                    AllCondition => holding == decided.Count,
                    AnyCondition => holding > 0,
                    NoneCondition => holding == 0,
                    _ => false,
                };

                // The parts that made it come out as it did: an all that holds needs every part, one that does not the
                // parts that failed it; an any is the other way round, and a none is an any that must not hold.
                bool? deciding = group switch
                {
                    AllCondition => held ? null : false,
                    AnyCondition => held ? true : null,
                    NoneCondition => held ? null : true,
                    _ => null,
                };

                return new Decision(held, [.. decided.Where(part => deciding is not { } wanted || part.Held == wanted).SelectMany(part => part.Tests)]);

            default:
                return null;
        }
    }

    private static string Words(ConditionOperator op) => op switch
    {
        ConditionOperator.Equals => "is",
        ConditionOperator.NotEquals => "is not",
        ConditionOperator.StartsWith => "starts with",
        ConditionOperator.Contains => "contains",
        ConditionOperator.NotContains => "does not contain",
        ConditionOperator.EndsWith => "ends with",
        ConditionOperator.Matches => "matches",
        ConditionOperator.In => "is one of",
        ConditionOperator.Greater => "is more than",
        ConditionOperator.GreaterOrEqual => "is at least",
        ConditionOperator.Less => "is less than",
        ConditionOperator.LessOrEqual => "is at most",
        ConditionOperator.InSubnet => "is in the network",
        _ => "is tested against",
    };

    // A MAC address as the web writes it, its bytes apart with colons, and the phase in words.
    private static string Shown(string variable, string actual)
    {
        if (string.Equals(variable, MachineVariableNames.Phase, StringComparison.OrdinalIgnoreCase))
        {
            return actual == nameof(SequencePhase.WindowsPE) ? "Windows PE" : actual;
        }

        return ConditionEvaluator.TypeOf(variable) == FactType.Mac && actual.Length == 12 && actual.All(char.IsAsciiHexDigit)
            ? string.Join(':', actual.Chunk(2).Select(pair => new string(pair)))
            : actual;
    }

    private sealed record Decision(bool Held, IReadOnlyList<string> Tests);
}
