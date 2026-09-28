// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// Sent back in the X-DDT-Reauthentication header when a save changes a field that a section's Reauthenticate list
// names.
public sealed record ReauthenticationToken(string Token, DateTimeOffset ExpiresUtc);
