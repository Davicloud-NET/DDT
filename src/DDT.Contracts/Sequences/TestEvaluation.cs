// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// One test of a node as it was decided, so a run can say why it took a path: "model 'Latitude 7440' contains
// 'Latitude'". Path is the test's field within the step as a SequenceProblem names it, such as "test.parts[1]".
public sealed record TestEvaluation(string Path, bool Held, string? Actual)
{
    // The engine keeps no more per node, and the server holds reports, which come from outside, to the same bounds.
    public const int MaxPerNode = 32;

    // Actual, the value tested against, is cut to this many characters; it is null when there was none.
    public const int MaxActualLength = 256;
}
