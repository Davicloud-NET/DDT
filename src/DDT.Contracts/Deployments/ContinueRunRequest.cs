// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Deployments;

// Continues the pause the page showed: the Pause step and its visit, so a click that comes late continues no later one.
public sealed record ContinueRunRequest(Guid StepId, int Pass);
