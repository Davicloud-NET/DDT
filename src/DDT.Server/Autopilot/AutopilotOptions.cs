// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Server.Autopilot;

// The Entra app registration that uploads hardware hashes through Microsoft Graph. Without all three, the server keeps
// hashes for the CSV export and uploads nothing.
public sealed class AutopilotOptions
{
    public const string SectionName = "DDT:Autopilot";

    // A GUID or a domain such as contoso.onmicrosoft.com.
    public string TenantId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    // Never stored in the section's document.
    [JsonIgnore]
    public string ClientSecret { get; set; } = string.Empty;

    // For a hash whose step set no group tag.
    public string DefaultGroupTag { get; set; } = string.Empty;

    public bool CanUpload() =>
        !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrEmpty(ClientSecret);
}
