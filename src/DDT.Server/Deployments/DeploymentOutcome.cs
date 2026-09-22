// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

public enum DeploymentOutcome
{
    Accepted,

    // A repeated terminal report: nothing to save, and the agent gets its tokens as if it had been.
    Unchanged,
    NotFound,
    Conflict,
    Invalid,

    // The run was ended because it cannot go on: the change is saved, and the caller answers 409 with the reason.
    Refused,
}
