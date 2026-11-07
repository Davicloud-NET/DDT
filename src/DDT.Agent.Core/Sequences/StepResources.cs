// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// What a step's accounts give it while it runs. Account is the account it runs as, signed in already, or null to run
// as the agent. SharePaths says where it finds its shares when the system gave them places of their own.
public sealed record StepResources(IAccountSession? Account, IReadOnlyList<string> SharePaths)
{
    public static StepResources None { get; } = new(null, []);
}
