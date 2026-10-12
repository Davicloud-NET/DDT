// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent;

// What a registration presents beside the machine's facts. No record: the tokens are secrets, and a record prints them.
internal sealed class RegistrationClaims
{
    public string? ResumeToken { get; init; }

    public string? RunToken { get; init; }

    // A run found handed over on a disk without its token, which the agent asks the server about.
    public AgentHandedOverRun? HandedOver { get; init; }

    // Someone at the machine confirmed to start over, which ends the run that goes on in the installed system.
    public bool StartsOver { get; init; }

    // How long the server is tried before the registration gives up. Null to try until it answers.
    public TimeSpan? GiveUpAfter { get; init; }
}
