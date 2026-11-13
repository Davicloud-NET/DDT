// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Agents;

// Fetched as a run in Windows ends, kept only in memory, and set up once DDT's session is gone.
public sealed record AgentFirstLogon(IReadOnlyList<string> Commands, AgentAutoLogon? AutoLogon)
{
    [JsonIgnore]
    public bool IsEmpty => Commands.Count == 0 && AutoLogon is null;
}
