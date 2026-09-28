// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// Value is null when the setting is not shown, because it is secret by rule or not on the list of values that are.
// Source names where it comes from, such as an environment variable or appsettings.json, and is null when unset.
public sealed record ServerSetting(string Key, string? Value, bool IsSet, string? Source, bool Secret);
