// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;

namespace DDT.Contracts.Agents;

public sealed record AgentRegistrationResult(
    Guid MachineId,
    MachineState State,
    // A poll token while the machine waits for approval, and a session token once it's approved. Both tokens are null
    // for a rejected machine, so it has nothing left to present.
    string? Token,
    string? ResumeToken,
    int PollAfterSeconds,
    // Whoever signed in at the machine, so an agent waiting for a web approval does not ask again.
    string? SignedInBy,
    // Set when the registration resumed a running run, using its RunToken or its ResumeToken.
    Guid? RunId = null,
    // The run token to keep once RunId is set.
    string? RunToken = null,
    // The language the console at the machine starts in, en or de. Null means the language of WinPE.
    string? ConsoleLanguage = null,
    // The logo the console shows. The agent downloads it from AgentRoutes.ConsoleLogo. Null when there's no logo.
    string? ConsoleLogoSha256 = null);
