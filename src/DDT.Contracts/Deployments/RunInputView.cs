// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Contracts.Deployments;

// An input of a run and whether it's answered. The answer itself is in the run's values, and an Account input's
// answer never leaves the server. AnsweredBy is who answered, a user's name or the machine.
public sealed record RunInputView(AgentInput Input, bool Answered, string? AnsweredBy);
