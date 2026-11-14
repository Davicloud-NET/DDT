// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// What a BitLocker step does on a machine without a TPM, or in a boot image without manage-bde.
public enum BitLockerUnavailable
{
    // The step succeeds with a warning.
    Skip,
    Fail,
}
