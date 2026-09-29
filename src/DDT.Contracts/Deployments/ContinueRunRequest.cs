// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Deployments;

// Continues the pause the page showed. It names the Pause step and its pass, so a late click can't continue a later
// pause.
public sealed record ContinueRunRequest(Guid StepId, int Pass);
