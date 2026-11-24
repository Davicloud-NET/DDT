// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Servicing;

// A Windows package of the step and the folder its .msu and .cab files are unpacked in.
public sealed record UnpackedPackage(AgentRunPackage Package, string Folder);
