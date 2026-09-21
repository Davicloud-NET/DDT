// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;

namespace DDT.Contracts.Agents;

// Token is a poll token while the machine waits for approval and a session token once approved. Both
// tokens are null when the machine has been rejected: it gets nothing further to present. SignedInBy names
// whoever signed in at the machine, so an agent that is waiting for a web approval does not ask again.
public sealed record AgentRegistrationResult(
    Guid MachineId,
    MachineState State,
    string? Token,
    string? ResumeToken,
    int PollAfterSeconds,
    string? SignedInBy);
