// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

public enum AgentBinarySource
{
    // Machines keep the agent of their boot image.
    None,

    // Uploaded on the settings page.
    Uploaded,

    // DDT:Agent:BinaryPath names the file, which the page then cannot replace.
    Configuration,
}
