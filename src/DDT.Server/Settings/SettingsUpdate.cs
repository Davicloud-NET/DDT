// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Nodes;
using DDT.Contracts.Settings;

namespace DDT.Server.Settings;

// A save of one section.
public sealed class SettingsUpdate
{
    public required long Version { get; init; }

    // The whole section as its option class serializes it; a field configuration locks is not written.
    public required JsonObject Values { get; init; }

    // A secret missing here is kept.
    public IReadOnlyDictionary<string, SecretUpdate> Secrets { get; init; } = new Dictionary<string, SecretUpdate>();

    public IReadOnlySet<string> Confirmed { get; init; } = new HashSet<string>();

    public bool Reauthenticated { get; init; }

    // The token an LDAP test gives a directory administrator for exactly these values.
    public string? DirectoryProof { get; init; }
}
