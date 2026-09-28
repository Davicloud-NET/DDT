// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Deployments;

// The Pause step a run waits at, and its pass. A ContinueRunRequest names both. The agent fills in the values in
// Message. ContinuesUtc is when the pause continues by itself, or null when it waits for someone.
public sealed record RunPauseView(Guid StepId, int Pass, string? Message, DateTimeOffset? SinceUtc, DateTimeOffset? ContinuesUtc);
