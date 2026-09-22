// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Sequences;

namespace DDT.Server.Sequences;

// The stored form of a definition is exactly the contract document, written by DdtJsonContext.
public static class SequenceDocuments
{
    public static string Write(SequenceDefinition definition) =>
        JsonSerializer.Serialize(definition, DdtJsonContext.Default.SequenceDefinition);

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

        if (definition.Steps.Any(step => step is null))
        {
            return "A step of the sequence is empty.";
        }

        if (definition.Steps.Count > SequenceLimits.MaxStoredSteps)
        {
            return $"A sequence can have at most {SequenceLimits.MaxStoredSteps} steps.";
        }

        return Encoding.UTF8.GetByteCount(Write(definition)) > SequenceLimits.MaxDefinitionBytes
            ? $"A sequence can have at most {SequenceLimits.MaxDefinitionBytes / 1024} KiB."
            : null;
    }
}
