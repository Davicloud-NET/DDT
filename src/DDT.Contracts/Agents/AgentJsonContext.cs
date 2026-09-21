// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;
using DDT.Contracts.Sequences;

namespace DDT.Contracts.Agents;

// Separate from DdtJsonContext so the NativeAOT agent carries metadata only for what it sends. Out of order metadata:
// a sequence the server kept in PostgreSQL jsonb has each step's "kind" after its "id".
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    AllowOutOfOrderMetadataProperties = true)]
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
[JsonSerializable(typeof(SequenceDefinition))]
[JsonSerializable(typeof(SequenceState))]
[JsonSerializable(typeof(IReadOnlyList<AgentSequenceChoice>))]
[JsonSerializable(typeof(AgentRunRequest))]
[JsonSerializable(typeof(AgentRun))]
[JsonSerializable(typeof(AgentRunReport))]
[JsonSerializable(typeof(AgentRunReportResult))]
[JsonSerializable(typeof(AgentJoinDomainCredentials))]
public sealed partial class AgentJsonContext : JsonSerializerContext;
