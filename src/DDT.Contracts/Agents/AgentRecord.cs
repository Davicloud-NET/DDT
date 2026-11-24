// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Agents;

// What the running step hands the server, by kind. The discriminators never change.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AgentRecoveryKey), "recoveryKey")]
[JsonDerivedType(typeof(AgentHardwareHash), "hardwareHash")]
public abstract record AgentRecord;
