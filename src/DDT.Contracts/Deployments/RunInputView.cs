// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Contracts.Deployments;

// An input of a run and whether it has its answer; the answer itself is among the run's values, and an Account input's
// never leaves the server. AnsweredBy is who gave it, a user's name or the machine.
public sealed record RunInputView(AgentInput Input, bool Answered, string? AnsweredBy);
