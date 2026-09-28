// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// KeyRingReadable is false when this server can't read the key ring the stored secrets were encrypted with. Then it
// saves no settings at all. Server lists the read-only settings that only configuration decides.
public sealed record SettingsOverview(IReadOnlyList<SettingsSectionSummary> Sections, IReadOnlyList<ServerSetting> Server, bool KeyRingReadable);
