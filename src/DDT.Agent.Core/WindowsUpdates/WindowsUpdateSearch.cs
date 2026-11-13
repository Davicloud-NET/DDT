// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsUpdates;

// MicrosoftUpdate registers Microsoft Update, which stays registered, and searches it instead of Windows Update alone.
public sealed record WindowsUpdateSearch(bool MicrosoftUpdate);
