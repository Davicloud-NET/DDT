// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Agents;

// Separate from DdtJsonContext so the NativeAOT agent carries metadata only for what it sends.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AgentRegistration))]
[JsonSerializable(typeof(AgentRegistrationResult))]
[JsonSerializable(typeof(AgentNextResult))]
[JsonSerializable(typeof(AgentLogBatch))]
[JsonSerializable(typeof(AgentSignInRequest))]
[JsonSerializable(typeof(AgentSignInResult))]
[JsonSerializable(typeof(AgentRelease))]
[JsonSerializable(typeof(IReadOnlyList<AgentImageChoice>))]
[JsonSerializable(typeof(AgentPickRequest))]
[JsonSerializable(typeof(AgentDeployment))]
[JsonSerializable(typeof(AgentDeploymentReport))]
[JsonSerializable(typeof(AgentDeploymentReportResult))]
public sealed partial class AgentJsonContext : JsonSerializerContext;
