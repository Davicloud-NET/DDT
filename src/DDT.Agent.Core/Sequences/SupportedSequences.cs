// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

// The sequences this agent runs. It tells the server the version, and the server hands it nothing newer.
public static class SupportedSequences
{
    // Version 5 adds steps in the installed Linux, which the agent for Linux runs there.
    public const int Version = 5;
}
