// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Deployments;

// The Pause step a run waits at, and its visit, which a ContinueRunRequest names. Message is worked out by the agent.
// ContinuesUtc is when the pause goes on by itself, null when it waits for someone.
public sealed record RunPauseView(Guid StepId, int Pass, string? Message, DateTimeOffset? SinceUtc, DateTimeOffset? ContinuesUtc);
