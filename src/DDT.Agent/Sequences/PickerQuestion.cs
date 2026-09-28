// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

internal enum PickerQuestion
{
    None,
    Sequence,
    Disk,
    ComputerName,

    // The sequence's inputs asked at the machine, all on one page.
    Inputs,
    Confirmation,

    // ANYWAY, for a disk image that will not start with the Secure Boot the machine has on.
    SecureBoot,
}
