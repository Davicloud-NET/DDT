// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Sequences;

namespace DDT.Core.Values;

// Everything a run's values are worked out from, in the order they win. That's answers, the machine, rules, roles, the
// sequence's defaults and the deployment defaults. Rules, roles and the sequence's variables hold templates.
public sealed record ValueSources
{
    // The sequence's inputs and variables. Their defaults win in that order. Null for a preview without a sequence.
    public SequenceDefinition? Sequence { get; init; }

    // By input name, ignoring case. An empty answer counts as none. Account inputs are never here, because their
    // answers are credentials and never become values.
    public IReadOnlyDictionary<string, string> Answers { get; init; } = new Dictionary<string, string>();

    // The machine's values. Its assigned name is ComputerName.
    public IReadOnlyList<NamedValue> Machine { get; init; } = [];

    // The rules that match the machine, from the top.
    public IReadOnlyList<ValueSet> Rules { get; init; } = [];

    // The machine roles, in the order the rules gave them.
    public IReadOnlyList<ValueSet> Roles { get; init; } = [];

    public IReadOnlyList<NamedValue> DeploymentDefaults { get; init; } = [];

    // What templates can read besides the values. Facts are never values themselves.
    public MachineVariables? Facts { get; init; }
}
