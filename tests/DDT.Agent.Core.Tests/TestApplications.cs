// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Applications;

namespace DDT.Agent.Tests;

// Applications as the server freezes them for a run.
internal static class TestApplications
{
    public static readonly AgentApplication Runtime = Command("0193a4b2-0000-7000-8000-00000000a001", "Runtime");
    public static readonly AgentApplication Tool = Command("0193a4b2-0000-7000-8000-00000000a002", "Tool");
    public static readonly AgentApplication Viewer = Command("0193a4b2-0000-7000-8000-00000000a003", "Viewer");

    // Success with 0, a restart with 3010, nothing to download and no detection rule.
    public static AgentApplication Command(string id, string name) =>
        new(Guid.Parse(id), name, ApplicationKind.Command, $"{name}.exe /S", [0], [3010], null, 5, false, null, null, null, []);
}
