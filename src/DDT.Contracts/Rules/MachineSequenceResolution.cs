// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;

namespace DDT.Contracts.Rules;

// The sequence a machine would get and why, for the page to explain. A rule only chooses: the machine still needs
// an approval or a sign-in. ProblemCount above zero means the chosen sequence cannot run until it is fixed. Explanation
// is English; ExplanationCode and ExplanationArgs are the same sentence for a client that says it in the person's
// language.
//
// The members after ExplanationArgs preview what a run of that sequence would start with. MatchedRuleIds are the rules
// that match the machine, top first, whether they chose the sequence or only set values or gave machine roles. Values
// are the values the run would have, each with where it comes from, as ResolvedValue says; a run that is running shows
// the values it started with. Inputs are the chosen sequence's inputs, and InputDefaults what their questions start
// with, as ValueResolution says. ValueProblems would keep the run from starting as things are now; a problem's StepId
// is null and its Field the value's or the input's name.
//
// Two resolutions are equal when they say the same: the code and its values say it again, and a dictionary compares
// only by reference. The preview is not compared.
public sealed record MachineSequenceResolution(
    SequenceResolutionSource Source,
    Guid? SequenceId,
    string? SequenceName,
    Guid? RuleId,
    int ProblemCount,
    string Explanation,
    string? ExplanationCode = null,
    IReadOnlyDictionary<string, object>? ExplanationArgs = null,
    IReadOnlyList<Guid>? MatchedRuleIds = null,
    IReadOnlyList<ResolvedValue>? Values = null,
    IReadOnlyList<InputDeclaration>? Inputs = null,
    IReadOnlyList<ResolvedValue>? InputDefaults = null,
    IReadOnlyList<SequenceProblem>? ValueProblems = null)
{
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
