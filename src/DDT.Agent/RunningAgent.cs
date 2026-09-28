// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// This agent as an update sees it. ConsolePath is the console beside it, which the server's replaces; null without one
// or when --console named one, which stays. ExecutablePath, Environment.ProcessPath unless set, starts for a new console.
public sealed record RunningAgent(
    string Sha256,
    string Directory,
    IReadOnlyList<string> Arguments,
    string? ConsolePath = null,
    string? ExecutablePath = null);
