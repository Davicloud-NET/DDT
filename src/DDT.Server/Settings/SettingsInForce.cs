// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Pxe;
using DDT.Server.Authentication;
using DDT.Server.Ldap;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Settings;

// The typed settings a snapshot hands its consumers, with what is safe in place of a section that has problems.
internal sealed record SettingsInForce(
    MachinePolicy Machines,
    LdapOptions Ldap,
    OidcOptions Oidc,
    ForwardedHeadersOptions ForwardedHeaders,
    PxeOptions? Pxe,
    IReadOnlyDictionary<string, LogLevel> LogLevels);
