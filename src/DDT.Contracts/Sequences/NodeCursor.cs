// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Where a run of a tree continues. It either enters NodeId, or, with Leaving, leaves NodeId once its body is done.
// That's the point where a repeat tests Until. A run whose cursor is null has ended.
public sealed record NodeCursor(Guid NodeId, bool Leaving);
