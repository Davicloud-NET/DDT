// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// agent.json, which Build-BootImage.ps1 writes beside the agent; KeyboardLayout is the image's, shown at the sign-in.
// Frozen: newer agents read the files of older builds, so fields stay optional and keep their names.
public sealed record AgentConfiguration(string? ServerUrl, string? RootCertificate, string? KeyboardLayout);
