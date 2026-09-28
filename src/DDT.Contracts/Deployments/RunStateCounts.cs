// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Deployments;

// How many of the runs a history query matches are in each state, regardless of the states it filtered for.
public sealed record RunStateCounts(int Assigned, int Running, int Done, int Failed, int Cancelled);
