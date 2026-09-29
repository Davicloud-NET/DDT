// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Settings;

// A secret in the Secrets column. Protected is null for one that was cleared.
internal sealed record StoredSecretDocument(string? Protected, DateTimeOffset UpdatedUtc);
