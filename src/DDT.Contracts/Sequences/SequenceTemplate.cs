// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// A starting point for a new sequence. Its steps get new ids every time it is read.
public sealed record SequenceTemplate(string Key, string Name, string Description, SequenceDefinition Definition);
