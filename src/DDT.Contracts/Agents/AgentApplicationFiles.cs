// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// Fetched from the run's files route by Sha256. ExpandedBytes is what the zip unpacks to.
public sealed record AgentApplicationFiles(string Sha256, long SizeBytes, long ExpandedBytes, string Name);
