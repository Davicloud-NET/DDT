// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Server.Sequences;

// The parts of a sequence an administrator edits, before or after a save.
public sealed record SequenceContent(string Name, string? Description, SequenceDefinition Definition);
