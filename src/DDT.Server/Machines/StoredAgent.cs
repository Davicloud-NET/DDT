// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Settings;

namespace DDT.Server.Machines;

// An agent file on the server: where it is, what machines are told about it, its file version if it has one, and
// whether it was uploaded, came with the server or is named in configuration.
public sealed record StoredAgent(string Path, AgentRelease Release, Version? Version, AgentBinarySource Source);
