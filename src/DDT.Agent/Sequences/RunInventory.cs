// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// Every step on every branch of a run, and the images they apply and write: which branch a run takes it finds out
// only as it goes, and nothing may be erased for a run that could not finish on one of them.
internal sealed record RunInventory(
    IReadOnlyList<SequenceStep> Steps,
    IReadOnlyList<AgentRunImage> Images,
    IReadOnlyList<AgentRunImage> RawImages);
