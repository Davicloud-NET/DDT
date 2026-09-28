// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// ConfigurationKey and EnvironmentVariable spell the key that sets the field, such as DDT:Deployment:Domain:Name and
// DDT__Deployment__Domain__Name. StoredDiffers means the value saved on the page differs. That value applies again
// once the key is removed.
public sealed record SettingLock(string Field, string ConfigurationKey, string EnvironmentVariable, string Source, bool StoredDiffers);
