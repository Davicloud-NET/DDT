// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Sequences;

namespace DDT.Core.Values;

// Everything a run's values are worked out from, in the order they win: the answers to the sequence's inputs, the
// machine's own values (its assigned name is ComputerName), the rules that match it from the top, the machine roles in
// the order those rules gave them, the sequence's defaults (its inputs' first, then its variables'), and the deployment
// defaults. Rules, machine roles and the sequence's variables hold templates; answers, the machine's values, the inputs'
// defaults and the deployment defaults are taken as they are. Facts are what templates read besides the values, and
// never a value themselves.
public sealed record ValueSources
{
    // The sequence's variables and inputs; null for a preview without one.
    public SequenceDefinition? Sequence { get; init; }

    // By input name, ignoring case. An empty answer is none. Account inputs are never here: their answers are
    // credentials, which never become values.
    public IReadOnlyDictionary<string, string> Answers { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<NamedValue> Machine { get; init; } = [];

    public IReadOnlyList<ValueSet> Rules { get; init; } = [];

    public IReadOnlyList<ValueSet> Roles { get; init; } = [];

    public IReadOnlyList<NamedValue> DeploymentDefaults { get; init; } = [];

    public MachineVariables? Facts { get; init; }
}

// A rule or a machine role and the values it sets, in its order.
public sealed record ValueSet(Guid Id, string Name, IReadOnlyList<NamedValue> Values);
