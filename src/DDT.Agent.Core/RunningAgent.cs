// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// This agent as an update sees it. ConsolePath is the console next to it, which the server's console replaces. It's
// null when there's none, or when --console named one, because that one is kept. ExecutablePath is started again for
// a new console. It defaults to Environment.ProcessPath.
public sealed record RunningAgent(
    string Sha256,
    string Directory,
    IReadOnlyList<string> Arguments,
    string? ConsolePath = null,
    string? ExecutablePath = null);
