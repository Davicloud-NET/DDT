// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Version is the document schema, raised when a step kind or a member older agents would ignore is added. The server
// hands a run only to an agent whose AgentRegistration.SequenceVersion is at least this, and stores every sequence with
// the lowest version it needs, so an older agent still gets the sequences it can run.
public sealed record SequenceDefinition(int Version, IReadOnlyList<SequenceStep> Steps)
{
    // 2 adds WriteRawImageStep and WriteCloudInitSeedStep. 3 makes the steps a tree (group, if, repeat), adds Set
    // variable and Pause, When, Shares, RunAs, a join's Account, Variables, Inputs and the operators after Contains.
    public const int CurrentVersion = 3;

    // Null is none. Left out of the JSON while unset, so a document without them reads and writes as before.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<VariableDeclaration>? Variables { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<InputDeclaration>? Inputs { get; init; }

    // Steps may hold null when the document came from outside.
    public int RequiredVersion() => SequenceTree.RequiredVersion(this);

    // The form the server stores: the lowest version, empty lists of the version 3 members as none, and a When that
    // older agents could run as it is (an all of tests with the operators and variables of version 1) moved into
    // Conditions, so a flat document edited in the flow builder still runs on agents of versions 1 and 2.
    public SequenceDefinition Normalised()
    {
        SequenceDefinition folded = SequenceTree.Fold(this);

        return folded with { Version = folded.RequiredVersion() };
    }
}
