// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// The agent.json that Build-BootImage.ps1 writes next to the agent. KeyboardLayout is the boot image's layout, shown
// at the sign-in. The format is frozen. Newer agents read the files of older builds, so fields stay optional and keep
// their names.
public sealed record AgentConfiguration(string? ServerUrl, string? RootCertificate, string? KeyboardLayout);
