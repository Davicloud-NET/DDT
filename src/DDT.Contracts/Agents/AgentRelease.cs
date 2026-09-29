// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// Frozen. Agents inside old boot images read this, so its members are never renamed or removed.
public sealed record AgentRelease(string Sha256, long Size);
