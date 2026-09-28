// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;

namespace DDT.Contracts.Agents;

public sealed record AgentRegistrationResult(
    Guid MachineId,
    MachineState State,
    // A poll token while the machine waits for approval, a session token once approved. Both tokens are null once the
    // machine is rejected: it gets nothing further to present.
    string? Token,
    string? ResumeToken,
    int PollAfterSeconds,
    // Whoever signed in at the machine, so an agent waiting for a web approval does not ask again.
    string? SignedInBy,
    // Set when the registration continued a running run, by its RunToken or its ResumeToken.
    Guid? RunId = null,
    // The one to keep once RunId is set.
    string? RunToken = null,
    // The language the console at the machine starts in, en or de; null for the language of Windows PE.
    string? ConsoleLanguage = null,
    // The logo the console shows, which the agent downloads from AgentRoutes.ConsoleLogo; null when there is none.
    string? ConsoleLogoSha256 = null);
