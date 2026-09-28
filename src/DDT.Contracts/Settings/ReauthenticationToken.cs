// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// Sent back in the X-DDT-Reauthentication header of a save that changes a field listed in a section's reauthenticate.
public sealed record ReauthenticationToken(string Token, DateTimeOffset ExpiresUtc);
