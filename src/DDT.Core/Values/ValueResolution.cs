// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Values;

namespace DDT.Core.Values;

// What ValueResolver worked out. Problems keep a run from starting.
public sealed record ValueResolution(
    // Every value a source set, grouped by name: the one used, then those it overrode. One that could not be worked out
    // is here as written, and not in Effective.
    IReadOnlyList<ResolvedValue> Values,
    // The values used, by name ignoring case, for the run and its templates.
    IReadOnlyDictionary<string, string> Effective,
    IReadOnlyList<ValueProblem> Problems,
    // What an input's question starts with, in the order of the inputs: the machine's, a rule's or a role's value for
    // its name, else its own Default. Overridden marks one an answer overrides.
    IReadOnlyList<ResolvedValue> InputDefaults);
