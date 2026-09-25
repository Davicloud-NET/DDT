// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Version is the document schema, raised when a step kind is added. An agent throws on a kind it does not know, so
// the server hands a run only to an agent whose AgentRegistration.SequenceVersion is at least this. The server stores
// every sequence with the lowest version its kinds need, so an older agent still gets the sequences it can run.
public sealed record SequenceDefinition(int Version, IReadOnlyList<SequenceStep> Steps)
{
    // 2 adds WriteRawImageStep and WriteCloudInitSeedStep.
    public const int CurrentVersion = 2;

    // Steps may hold null when the document came from outside.
    public int RequiredVersion() =>
        Math.Max(1, Steps?.Where(step => step is not null).Select(step => step.MinimumVersion).DefaultIfEmpty(1).Max() ?? 1);

    public SequenceDefinition Normalised() => this with { Version = RequiredVersion() };
}
