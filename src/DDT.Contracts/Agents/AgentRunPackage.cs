// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// A driver or script zip for the step that uses it. A driver step can have several, matched to the machine's model.
public sealed record AgentRunPackage(Guid StepId, string Name, string Sha256, long SizeBytes);
