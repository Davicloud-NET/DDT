// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Settings;

namespace DDT.Server.Settings;

// View is the section as it applies after a save that went through, and null otherwise.
internal sealed record SettingsSaved<TValues>(SettingsSaveResult Result, SettingsSectionView<TValues>? View);
