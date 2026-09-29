// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// Value is null when the setting isn't shown, either because it's secret by rule or because it isn't on the list of
// shown values. Source names where it comes from, such as an environment variable or appsettings.json, and is null
// when unset.
public sealed record ServerSetting(string Key, string? Value, bool IsSet, string? Source, bool Secret);
