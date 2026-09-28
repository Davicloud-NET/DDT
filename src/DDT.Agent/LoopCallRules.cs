// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;

namespace DDT.Agent;

// How the loop treats a failed server call while the machine registers and waits, before any run. ServerCallRules
// judges the calls of a run.
internal static class LoopCallRules
{
    // JsonException covers an HTML page from a wrong URL and a newer server reporting a state this agent
    // does not know; neither may end the agent.
    public static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or TimeoutException or TaskCanceledException or JsonException;
}
