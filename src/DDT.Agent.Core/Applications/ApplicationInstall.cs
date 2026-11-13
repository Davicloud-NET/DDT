// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Core.Sequences;

namespace DDT.Agent.Applications;

// One application of a step's turn. Attempt is 1, or 2 when the machine restarted while its installer ran.
public sealed record ApplicationInstall(AgentApplication Application, Guid StepId, StepContext Context, int Attempt);
