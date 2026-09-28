// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Data;

// One section of the settings page as stored.
public sealed class SettingsSection
{
    public const int MaxNameLength = 32;

    public required string Section { get; set; }

    public int SchemaVersion { get; set; }

    // Only the fields that were ever written, as JSON of the section's option class.
    public string Values { get; set; } = "{}";

    // Each secret field's ciphertext.
    public string Secrets { get; set; } = "{}";

    // Checked on every save, so two saves never overwrite each other unnoticed; other processes reload when it grows.
    public long Version { get; set; }

    public DateTimeOffset? UpdatedUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string? UpdatedByName { get; set; }
}
