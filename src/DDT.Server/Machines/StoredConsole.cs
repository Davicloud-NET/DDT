// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Settings;

namespace DDT.Server.Machines;

// A console zip on the server. The version is that of ddt-console.exe.
public sealed record StoredConsole(string Path, ConsoleRelease Release, Version? Version, AgentBinarySource Source);
