// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Applications;

// What a kind's installer starts from: the unpacked files or the run's scripts folder as WorkingDirectory, Scripts for
// a file it writes, Logs for a tool's log, and the variables its process gets.
public sealed record ApplicationLaunch(
    AgentApplication Application,
    string WorkingDirectory,
    string Scripts,
    string Logs,
    IReadOnlyDictionary<string, string> Environment);
