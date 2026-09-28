// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Settings;

// A section as it is stored and as a save would leave it.
public sealed record SettingsSectionChange(SettingsSectionState Before, SettingsSectionState After);
