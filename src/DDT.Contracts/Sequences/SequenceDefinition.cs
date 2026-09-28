// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Version is the document schema. It goes up when a step kind is added, or a member that older agents would ignore.
// The server only gives a run to an agent whose AgentRegistration.SequenceVersion is at least this. It stores every
// sequence with the lowest version it needs, so an older agent still gets the sequences it can run.
public sealed record SequenceDefinition(int Version, IReadOnlyList<SequenceStep> Steps)
{
    // Version 2 adds WriteRawImageStep and WriteCloudInitSeedStep. Version 3 makes the steps a tree (group, if,
    // repeat). It also adds Set variable, Pause, When, Shares, RunAs, a join's Account, Variables, Inputs and the
    // operators after Contains.
    public const int CurrentVersion = 3;

    // Null is none. Left out of the JSON while unset, so a document without them reads and writes as before.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<VariableDeclaration>? Variables { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<InputDeclaration>? Inputs { get; init; }

    // Steps may hold null when the document came from outside.
    public int RequiredVersion() => SequenceTree.RequiredVersion(this);

    // The form the server stores. It has the lowest version, empty version 3 lists become null, and a When that older
    // agents can run moves into Conditions. Such a When is an all of tests with the operators and variables of
    // version 1. That way a flat document edited in the flow builder still runs on agents of versions 1 and 2.
    public SequenceDefinition Normalised()
    {
        SequenceDefinition folded = SequenceTree.Fold(this);

        return folded with { Version = folded.RequiredVersion() };
    }
}
