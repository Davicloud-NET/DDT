// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// One test of a node as it was decided, so a run can say why it took a path, such as "model 'Latitude 7440'
// contains 'Latitude'". Path is the test's field within the step, written like a SequenceProblem's field, such as
// "test.parts[1]".
public sealed record TestEvaluation(string Path, bool Held, string? Actual)
{
    // The engine keeps no more than this per node. Reports come from outside, so the server holds them to the same
    // limits.
    public const int MaxPerNode = 32;

    // Actual, the value that was tested, is cut to this many characters. It's null when there was no value.
    public const int MaxActualLength = 256;
}
