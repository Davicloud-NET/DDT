// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Where a node sits in its definition's tree. Body is the parent's member that holds the node, as StepBody names it.
// Like ParentId, it's null at the top. SiblingIndex, Order (within SequenceTree.Nodes) and Depth count from 0.
public sealed record NodePosition(SequenceStep Step, Guid? ParentId, string? Body, int SiblingIndex, int Order, int Depth);
