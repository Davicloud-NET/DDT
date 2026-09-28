// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Sequences;

namespace DDT.Core.Values;

// Everything a run's values are worked out from, in the order they win: answers, the machine, rules, roles, the
// sequence's defaults and the deployment defaults. Rules, roles and the sequence's variables hold templates.
public sealed record ValueSources
{
    // The sequence's inputs and variables, whose defaults win in that order; null for a preview without one.
    public SequenceDefinition? Sequence { get; init; }

    // By input name, ignoring case. An empty answer is none. Account inputs are never here: their answers are
    // credentials, which never become values.
    public IReadOnlyDictionary<string, string> Answers { get; init; } = new Dictionary<string, string>();

    // The machine's own values; its assigned name is ComputerName.
    public IReadOnlyList<NamedValue> Machine { get; init; } = [];

    // The rules that match the machine, from the top.
    public IReadOnlyList<ValueSet> Rules { get; init; } = [];

    // The machine roles, in the order the rules gave them.
    public IReadOnlyList<ValueSet> Roles { get; init; } = [];

    public IReadOnlyList<NamedValue> DeploymentDefaults { get; init; } = [];

    // What templates read besides the values, and never a value itself.
    public MachineVariables? Facts { get; init; }
}
