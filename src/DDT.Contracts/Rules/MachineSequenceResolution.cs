// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Rules;

// The sequence a machine would get and why, for the page to explain. A rule only chooses: the machine still needs
// an approval or a sign-in. ProblemCount above zero means the chosen sequence cannot run until it is fixed. Explanation
// is English; ExplanationCode and ExplanationArgs are the same sentence for a client that says it in the person's
// language.
//
// Two resolutions are equal when they say the same: the code and its values say it again, and a dictionary compares
// only by reference.
public sealed record MachineSequenceResolution(
    SequenceResolutionSource Source,
    Guid? SequenceId,
    string? SequenceName,
    Guid? RuleId,
    int ProblemCount,
    string Explanation,
    string? ExplanationCode = null,
    IReadOnlyDictionary<string, object>? ExplanationArgs = null)
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
