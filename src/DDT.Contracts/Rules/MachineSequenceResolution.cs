// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Rules;

// The sequence a machine would get and why, for the page to explain. A rule only chooses: the machine still needs
// an approval or a sign-in. ProblemCount above zero means the chosen sequence cannot run until it is fixed.
public sealed record MachineSequenceResolution(
    SequenceResolutionSource Source,
    Guid? SequenceId,
    string? SequenceName,
    Guid? RuleId,
    int ProblemCount,
    string Explanation);
