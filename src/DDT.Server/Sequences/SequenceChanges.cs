// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Sequences;

namespace DDT.Server.Sequences;

// The audit detail of a save: what changed, by step name and id, and the hash of every script that changed, so the
// log shows which code went out without holding the code. Each run keeps the full definition it ran.
public static class SequenceChanges
{
    private const int MaxDetailLength = 2048;

    public static string Describe(string oldName, string? oldDescription, SequenceDefinition before, string newName, string? newDescription, SequenceDefinition after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        StringBuilder detail = new();

        if (oldName != newName)
        {
            detail.Append($"Renamed from {oldName} to {newName}. ");
        }

        if (oldDescription != newDescription)
        {
            detail.Append("Changed the description. ");
        }

        // A draft can hold two steps with one id, so the first of each id stands for it.
        Dictionary<Guid, SequenceStep> old = ById(before);
        Dictionary<Guid, SequenceStep> current = ById(after);

        foreach (SequenceStep step in after.Steps.Where(s => !old.ContainsKey(s.Id)))
        {
            detail.Append($"Added {Label(step)}{ScriptHash(step)}. ");
        }

        foreach (SequenceStep step in before.Steps.Where(s => !current.ContainsKey(s.Id)))
        {
            detail.Append($"Removed {Label(step)}. ");
        }

        foreach (SequenceStep step in current.Values.Where(s => old.TryGetValue(s.Id, out SequenceStep? was) && Json(was) != Json(s)))
        {
            detail.Append($"Changed {Label(step)}{(Script(old[step.Id]) != Script(step) ? ScriptHash(step) : "")}. ");
        }

        Guid[] kept = [.. after.Steps.Select(s => s.Id).Where(old.ContainsKey).Distinct()];

        if (!kept.SequenceEqual(before.Steps.Select(s => s.Id).Where(current.ContainsKey).Distinct()))
        {
            detail.Append("Moved steps. ");
        }

        string text = detail.Length > 0 ? detail.ToString().TrimEnd() : "Saved without changes to the steps.";

        return text.Length <= MaxDetailLength ? text : text[..MaxDetailLength];
    }

    private static Dictionary<Guid, SequenceStep> ById(SequenceDefinition definition)
    {
        Dictionary<Guid, SequenceStep> steps = [];

        foreach (SequenceStep step in definition.Steps)
        {
            steps.TryAdd(step.Id, step);
        }

        return steps;
    }

    private static string Label(SequenceStep step) => $"{step.Name} ({step.Id:D})";

    private static string Json(SequenceStep step) => JsonSerializer.Serialize(step, DdtJsonContext.Default.SequenceStep);

    private static string? Script(SequenceStep step) => (step as RunScriptStep)?.Script;

    private static string ScriptHash(SequenceStep step) =>
        Script(step) is { } script ? $", script SHA-256 {Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(script)))}" : "";
}
