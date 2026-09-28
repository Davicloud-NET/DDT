// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// One test of a node as it was decided, so a run can say why it took a path: "model 'Latitude 7440' contains
// 'Latitude'". Path is the test's field within the step, as a SequenceProblem names it, such as "test.parts[1]" or
// "conditions[0]". Actual is the value it was tested against, null when there was none, cut to MaxActualLength
// characters. The engine keeps at most MaxPerNode per node, and the server holds reports, which come from outside, to
// the same bounds.
public sealed record TestEvaluation(string Path, bool Held, string? Actual)
{
    public const int MaxPerNode = 32;

    public const int MaxActualLength = 256;
}
