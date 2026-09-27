// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;
using DDT.Pxe;
using DDT.Server.Authentication;
using DDT.Server.Deployments;
using DDT.Server.Ldap;
using DDT.Server.Machines;
using DDT.Server.Security;

namespace DDT.Server.Settings;

// The stored document of each section is its option class, so an older build that meets a newer member skips it.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(DeploymentOptions))]
[JsonSerializable(typeof(MachineOptions))]
[JsonSerializable(typeof(LdapOptions))]
[JsonSerializable(typeof(OidcOptions))]
[JsonSerializable(typeof(DdtForwardedHeadersOptions))]
[JsonSerializable(typeof(PxeOptions))]
[JsonSerializable(typeof(LoggingOptions))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, BootTargetOptions>))]
[JsonSerializable(typeof(Dictionary<string, StoredSecretDocument>))]
[JsonSerializable(typeof(PxeHostDetail))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

// A secret in the Secrets column. Protected is null for one that was cleared.
internal sealed record StoredSecretDocument(string? Protected, DateTimeOffset UpdatedUtc);

// What a host that runs the pxe role found when it applied the section, in its row of ddt."SettingsHostStates".
internal sealed record PxeHostDetail(IReadOnlyList<PxeHostCandidate> Candidates, IReadOnlyList<string> Unmatched);

internal sealed record PxeHostCandidate(string Name, IReadOnlyList<string> Addresses, bool Served);
