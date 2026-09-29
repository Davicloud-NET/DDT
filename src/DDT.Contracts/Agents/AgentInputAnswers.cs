// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// Answers given at the machine while the run waits to start. The agent posts them to AgentRoutes.RunAnswers. The
// server only accepts answers to inputs asked at the machine.
public sealed record AgentInputAnswers(IReadOnlyList<InputAnswer> Answers);
