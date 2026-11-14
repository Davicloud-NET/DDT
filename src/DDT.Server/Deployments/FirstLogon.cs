// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Unattend;

namespace DDT.Server.Deployments;

// A Write answer file step's first-logon settings, worked out for a run. Account names whose password AutoLogon holds,
// for the audit.
public sealed record FirstLogon(IReadOnlyList<string> Commands, AutoLogonAccount? AutoLogon, string? Account);
