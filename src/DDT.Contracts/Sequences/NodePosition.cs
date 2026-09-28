// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Where a node sits in its definition's tree. ParentId and Body are null at the top; Body is the parent's member that
// holds the node, as StepBody names it. SiblingIndex counts within that list, Order within SequenceTree.Nodes, both
// from 0, and Depth is 0 at the top.
public sealed record NodePosition(SequenceStep Step, Guid? ParentId, string? Body, int SiblingIndex, int Order, int Depth);
