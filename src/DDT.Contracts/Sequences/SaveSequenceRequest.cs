// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Revision is the one the client last read. A save over a newer revision is refused and returns the current view.
public sealed record SaveSequenceRequest(long Revision, string Name, string? Description, SequenceDefinition Definition);
