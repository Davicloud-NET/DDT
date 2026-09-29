// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Settings;

namespace DDT.Server.Tests;

// What a settings save sends besides the values: secret changes, the warnings it confirms, and the headers of a fresh
// proof of identity and of a directory proof.
internal sealed record SettingsSaveExtras(
    Dictionary<string, SecretUpdate>? Secrets = null,
    IReadOnlyList<string>? Confirm = null,
    string? Reauthentication = null,
    string? Proof = null);
