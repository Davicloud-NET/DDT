// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The autopilot section: the app registration that uploads hardware hashes. Its secret is clientSecret.
public sealed record AutopilotSettings(string TenantId, string ClientId, string DefaultGroupTag);
