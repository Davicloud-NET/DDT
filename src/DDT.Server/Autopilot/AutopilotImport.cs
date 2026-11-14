// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Autopilot;

// An imported device as Autopilot reports it. Error is Autopilot's own words for a failed import.
public sealed record AutopilotImport(string Id, AutopilotImportStatus Status, string? Error);
