// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Applications;

// How one application ended, with the log's sentence about it.
public sealed record ApplicationOutcome(ApplicationState State, string Message, bool RestartDue = false, int? ExitCode = null);
