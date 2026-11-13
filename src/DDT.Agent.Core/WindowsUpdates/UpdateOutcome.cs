// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsUpdates;

// One update's own result in a download or an installation. HResult is 0 unless it failed.
public sealed record UpdateOutcome(string Id, UpdateResult Result, int HResult = 0, bool RestartNeeded = false);
