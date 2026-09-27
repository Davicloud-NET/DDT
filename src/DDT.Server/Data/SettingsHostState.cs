// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Data;

// Whether a host applied a section that is applied by rebuilding a component in the running server: pxe, oidc and
// proxies. Only the process on that host writes its rows. For pxe, Detail holds the host's candidate interfaces.
public sealed class SettingsHostState
{
    public const int MaxHostLength = 128;
    public const int MaxMessageLength = 2048;

    public required string Host { get; set; }

    public required string Section { get; set; }

    public long AppliedVersion { get; set; }

    public SettingsApplyResult State { get; set; }

    public string? Message { get; set; }

    public string? Detail { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

public enum SettingsApplyResult
{
    Applied,
    Failed,
}
