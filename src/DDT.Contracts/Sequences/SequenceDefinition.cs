// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Version is the document schema, raised when a step kind is added. An agent throws on a kind it does not know, so
// the server hands a run only to an agent whose AgentRegistration.SequenceVersion is at least this.
public sealed record SequenceDefinition(int Version, IReadOnlyList<SequenceStep> Steps)
{
    public const int CurrentVersion = 1;
}
