// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsPhase;

// A registry value under HKEY_LOCAL_MACHINE as it was: a string, a number, or neither when there was none.
public sealed record SavedSetting(string Key, string Name, string? Text, int? Number);
