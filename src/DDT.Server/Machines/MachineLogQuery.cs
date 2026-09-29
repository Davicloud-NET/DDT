// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

// A page of a machine's log, from the route and the query string. It holds the newest lines, the lines before the first
// one a page showed, or the lines after the last one, such as a machineLogAppended push names. DeploymentId limits it
// to one run's lines.
internal sealed record MachineLogQuery(Guid Id, long? Before, long? After, int? Limit, Guid? DeploymentId);
