// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Agents;

// The boot environment a machine is about to restart into, and the sequence picked at it that starts there.
public sealed record AgentEnvironmentRequest(
    AgentEnvironment Environment,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? SequenceId = null);
