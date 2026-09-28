// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// An empty group: all and none hold, any does not.
public abstract record ConditionGroup : ConditionNode
{
    public IReadOnlyList<ConditionNode> Parts { get; init; } = [];
}
