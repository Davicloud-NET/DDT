// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Values;

namespace DDT.Core.Values;

// What ValueResolver worked out. Problems keep a run from starting.
public sealed record ValueResolution(
    // Every value a source set, grouped by name. The one used comes first, then those it overrode. A value that
    // couldn't be worked out is here as written, and missing from Effective.
    IReadOnlyList<ResolvedValue> Values,
    // The values used, by name ignoring case, for the run and its templates.
    IReadOnlyDictionary<string, string> Effective,
    IReadOnlyList<ValueProblem> Problems,
    // The value each input's question starts with, in input order. That's the machine's, a rule's or a role's value
    // for its name, or else the input's Default. Overridden marks one that an answer overrides.
    IReadOnlyList<ResolvedValue> InputDefaults);
