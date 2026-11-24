// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// Where the machine's hardware hash stands with Autopilot.
public enum HardwareHashUploadState
{
    // Not to be uploaded, or nothing set up to upload it.
    None,

    Pending,

    Imported,

    Failed,
}
