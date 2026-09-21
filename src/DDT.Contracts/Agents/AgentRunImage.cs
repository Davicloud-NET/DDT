// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// ImageId is the one an ApplyImage step names. The agent downloads the file by its Sha256 from AgentRoutes.RunFile.
public sealed record AgentRunImage(Guid ImageId, string Name, string Sha256, long SizeBytes, int WimIndex, long InstalledBytes);
