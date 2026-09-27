// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The section logging: a level such as Information or Debug per category, and Default for every other category.
public sealed record LoggingSettings(IReadOnlyDictionary<string, string> LogLevel);
