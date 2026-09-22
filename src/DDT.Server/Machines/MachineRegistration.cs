// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Server.Machines;

// Result is null when the registration was refused.
public sealed record MachineRegistration(AgentRegistrationResult? Result, RegistrationRefusal Refusal)
{
    public static MachineRegistration Refused(RegistrationRefusal refusal) => new(null, refusal);
}
