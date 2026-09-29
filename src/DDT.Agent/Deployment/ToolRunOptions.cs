// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Environment adds to the agent's variables, or to the account's when Account is set. A null option means the agent's
// working directory, no time limit, and running as the agent.
public sealed record ToolRunOptions(
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    TimeSpan? Timeout = null,
    IAccountSession? Account = null);
