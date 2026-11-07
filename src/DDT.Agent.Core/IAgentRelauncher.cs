// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

public interface IAgentRelauncher
{
    // Runs the agent at path in this console and returns its exit code once it has ended.
    Task<int> RunAsync(string path, IReadOnlyList<string> arguments);
}
