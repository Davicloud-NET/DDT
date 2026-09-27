// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// A starting point for a new sequence. Its steps get new ids every time it is read. Name and Description are English;
// their codes and values are the same texts for a client that says them in the person's language. The step names are
// the new sequence's own text, and stay English.
public sealed record SequenceTemplate(
    string Key,
    string Name,
    string Description,
    SequenceDefinition Definition,
    string? NameCode = null,
    IReadOnlyDictionary<string, object>? NameArgs = null,
    string? DescriptionCode = null,
    IReadOnlyDictionary<string, object>? DescriptionArgs = null);
