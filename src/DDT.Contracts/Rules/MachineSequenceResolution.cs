// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;

namespace DDT.Contracts.Rules;

// The sequence a machine would get and why, so the page can explain it. A rule only chooses the sequence. The
// machine still needs an approval or a sign-in.
public sealed record MachineSequenceResolution(
    SequenceResolutionSource Source,
    Guid? SequenceId,
    string? SequenceName,
    Guid? RuleId,
    // When above zero, the chosen sequence can't run until its problems are fixed.
    int ProblemCount,
    // In English. ExplanationCode and ExplanationArgs carry the same message, so a client can show it in the person's
    // language.
    string Explanation,
    string? ExplanationCode = null,
    IReadOnlyDictionary<string, object>? ExplanationArgs = null,
    // The members from here on preview what a run of that sequence would start with. MatchedRuleIds lists the rules
    // that match the machine, top first, whether they chose the sequence or only set values or gave machine roles.
    IReadOnlyList<Guid>? MatchedRuleIds = null,
    // The values the run would have, each with where it comes from. A running run shows the values it started with.
    IReadOnlyList<ResolvedValue>? Values = null,
    // The chosen sequence's inputs, and the defaults their questions are prefilled with.
    IReadOnlyList<InputDeclaration>? Inputs = null,
    IReadOnlyList<ResolvedValue>? InputDefaults = null,
    // What would keep the run from starting right now. A problem's StepId is null, and its Field is the name of the
    // value or input.
    IReadOnlyList<SequenceProblem>? ValueProblems = null)
{
    // Equal when they say the same thing. The code and its args only repeat Explanation, and a dictionary compares by
    // reference. The preview isn't compared.
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
