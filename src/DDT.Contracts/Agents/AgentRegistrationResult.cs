// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;

namespace DDT.Contracts.Agents;

// Token is a poll token while the machine waits for approval and a session token once approved. Both
// tokens are null when the machine has been rejected: it gets nothing further to present. SignedInBy names
// whoever signed in at the machine, so an agent that is waiting for a web approval does not ask again. RunId is set
// when the registration continued that running run, with its RunToken or its ResumeToken, and RunToken is then the
// one to keep. ConsoleLanguage is the language the console at the machine starts in, en or de, or null for the
// language of Windows PE. ConsoleLogoSha256 is the SHA-256 of the logo the console shows, which the agent downloads from
// AgentRoutes.ConsoleLogo, or null when there is none.
public sealed record AgentRegistrationResult(
    Guid MachineId,
    MachineState State,
    string? Token,
    string? ResumeToken,
    int PollAfterSeconds,
    string? SignedInBy,
    Guid? RunId = null,
    string? RunToken = null,
    string? ConsoleLanguage = null,
    string? ConsoleLogoSha256 = null);
