// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Data;

// One section of the settings page. Values holds only the fields that were ever written, as JSON of the section's
// option class; Secrets maps each secret field to its ciphertext. Version is checked on every save, so two saves of one
// section never overwrite each other unnoticed, and other processes reload the section when it grows.
public sealed class SettingsSection
{
    public const int MaxNameLength = 32;

    public required string Section { get; set; }

    public int SchemaVersion { get; set; }

    public string Values { get; set; } = "{}";

    public string Secrets { get; set; } = "{}";

    public long Version { get; set; }

    public DateTimeOffset? UpdatedUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string? UpdatedByName { get; set; }
}
