// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;

namespace DDT.Contracts.Rules;

// The sequence a machine would get and why, for the page to explain. A rule only chooses: the machine still needs
// an approval or a sign-in.
public sealed record MachineSequenceResolution(
    SequenceResolutionSource Source,
    Guid? SequenceId,
    string? SequenceName,
    Guid? RuleId,
    // Above zero, the chosen sequence cannot run until it is fixed.
    int ProblemCount,
    // English; ExplanationCode and ExplanationArgs say the same for a client in the person's language.
    string Explanation,
    string? ExplanationCode = null,
    IReadOnlyDictionary<string, object>? ExplanationArgs = null,
    // From here on, a preview of what a run of that sequence would start with. The rules that match the machine, top
    // first, whether they chose the sequence or only set values or gave machine roles.
    IReadOnlyList<Guid>? MatchedRuleIds = null,
    // The values the run would have, each with where it comes from; a running run shows the values it started with.
    IReadOnlyList<ResolvedValue>? Values = null,
    // The chosen sequence's inputs, and what their questions start with.
    IReadOnlyList<InputDeclaration>? Inputs = null,
    IReadOnlyList<ResolvedValue>? InputDefaults = null,
    // What would keep the run from starting as things are now; a problem's StepId is null and its Field the value's or
    // the input's name.
    IReadOnlyList<SequenceProblem>? ValueProblems = null)
{
    // Equal when they say the same: the code and its values repeat Explanation, and a dictionary compares only by
    // reference. The preview is not compared.
    public bool Equals(MachineSequenceResolution? other) =>
        other is not null
        && Source == other.Source
        && SequenceId == other.SequenceId
        && SequenceName == other.SequenceName
        && RuleId == other.RuleId
        && ProblemCount == other.ProblemCount
        && Explanation == other.Explanation;

    public override int GetHashCode() => HashCode.Combine(Source, SequenceId, SequenceName, RuleId, ProblemCount, Explanation);
}
