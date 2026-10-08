// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

// The program that runs a script, and its arguments.
public sealed record ScriptLaunch(string FileName, IReadOnlyList<string> Arguments)
{
    // What the script finds in its environment besides the agent's own variables and DDT's. Null for nothing more.
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}
