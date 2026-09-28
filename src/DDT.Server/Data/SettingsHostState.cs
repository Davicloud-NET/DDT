// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Data;

// Whether a host applied a section that takes effect by rebuilding a component in the running server: pxe, oidc or
// proxies. Only the process on that host writes its rows.
public sealed class SettingsHostState
{
    public const int MaxHostLength = 128;
    public const int MaxMessageLength = 2048;

    public required string Host { get; set; }

    public required string Section { get; set; }

    public long AppliedVersion { get; set; }

    public SettingsApplyResult State { get; set; }

    public string? Message { get; set; }

    // A JSON object: the message as a code, and for pxe the host's candidate interfaces (PxeHostDetail).
    public string? Detail { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}
