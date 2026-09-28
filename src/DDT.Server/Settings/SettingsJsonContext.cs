// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;
using DDT.Pxe;
using DDT.Server.Authentication;
using DDT.Server.Configuration;
using DDT.Server.Deployments;
using DDT.Server.Ldap;
using DDT.Server.Machines;
using DDT.Server.Security;

namespace DDT.Server.Settings;

// Each section's stored document is its option class, so an older build skips a member it doesn't know yet.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(DeploymentOptions))]
[JsonSerializable(typeof(MachineOptions))]
[JsonSerializable(typeof(LdapOptions))]
[JsonSerializable(typeof(OidcOptions))]
[JsonSerializable(typeof(DdtForwardedHeadersOptions))]
[JsonSerializable(typeof(PxeOptions))]
[JsonSerializable(typeof(LoggingOptions))]
[JsonSerializable(typeof(HttpsOptions))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, BootTargetOptions>))]
[JsonSerializable(typeof(Dictionary<string, StoredSecretDocument>))]
[JsonSerializable(typeof(PxeHostDetail))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
