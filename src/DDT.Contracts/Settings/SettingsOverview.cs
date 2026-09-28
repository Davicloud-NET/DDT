// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// KeyRingReadable: false when this server cannot read the key ring the stored secrets were encrypted with, and then it
// saves no settings at all. Server lists what configuration alone decides, read-only.
public sealed record SettingsOverview(IReadOnlyList<SettingsSectionSummary> Sections, IReadOnlyList<ServerSetting> Server, bool KeyRingReadable);
