// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// Who gave an account for a run: the user signed in on the web or at the machine.
public sealed record RunCredentialGiver(Guid? UserId, string? Name, bool AtMachine);
