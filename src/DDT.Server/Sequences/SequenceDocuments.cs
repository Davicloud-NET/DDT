// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Sequences;

namespace DDT.Server.Sequences;

// A definition is stored exactly as the contract document, written by DdtJsonContext. It gets the lowest version its
// step kinds need, so agents that don't know newer kinds can still read it.
public static class SequenceDocuments
{
    public static string Write(SequenceDefinition definition) =>
        JsonSerializer.Serialize(definition.Normalised(), DdtJsonContext.Default.SequenceDefinition);

    public static SequenceDefinition Read(string definition) =>
        JsonSerializer.Deserialize(definition, DdtJsonContext.Default.SequenceDefinition)
        ?? throw new InvalidDataException("A stored sequence definition is null.");

    // Why a document cannot be stored at all. Anything else, however wrong, is stored and reported as a problem.
    public static string? Malformation(SequenceDefinition? definition)
    {
        if (definition?.Steps is null)
        {
            return "The sequence has no list of steps.";
        }

        int nodes = 0;

        if (!Whole(definition.Steps, ref nodes))
        {
            return "A step of the sequence is empty.";
        }

        if (nodes > SequenceLimits.MaxStoredNodes)
        {
            return $"A sequence can have at most {SequenceLimits.MaxStoredNodes} steps, groups, IFs and repeats together.";
        }

        return Encoding.UTF8.GetByteCount(Write(definition)) > SequenceLimits.MaxDefinitionBytes
            ? $"A sequence can have at most {SequenceLimits.MaxDefinitionBytes / 1024} KiB."
            : null;
    }

    // Whether no list in the tree has a null entry. Counts the nodes along the way.
    private static bool Whole(IReadOnlyList<SequenceStep?> steps, ref int nodes)
    {
        foreach (SequenceStep? step in steps)
        {
            if (step is null)
            {
                return false;
            }

            nodes++;

            foreach (StepBody body in step.Bodies)
            {
                if (!Whole(body.Steps, ref nodes))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
