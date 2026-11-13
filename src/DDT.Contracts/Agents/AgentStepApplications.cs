// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// What an Install applications step installs, in order: its own list, then what the run's values added at its start.
public sealed record AgentStepApplications(IReadOnlyList<AgentApplication> Applications);
