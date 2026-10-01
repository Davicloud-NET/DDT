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

    // The one the server came with, next to its program. Offered while nothing is uploaded.
    Bundled,

    // DDT:Agent:BinaryPath names the file, so the page can't replace it.
    Configuration,
}
