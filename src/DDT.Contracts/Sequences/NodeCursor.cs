// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Where a run of a tree goes on: entering NodeId, or, with Leaving, leaving it once its body is done, which is when a
// repeat tests Until. A run whose cursor is null is at its end.
public sealed record NodeCursor(Guid NodeId, bool Leaving);
