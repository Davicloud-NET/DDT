// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// An empty all or none group holds, and an empty any group doesn't.
public abstract record ConditionGroup : ConditionNode
{
    public IReadOnlyList<ConditionNode> Parts { get; init; } = [];
}
