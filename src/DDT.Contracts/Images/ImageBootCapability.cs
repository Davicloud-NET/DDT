// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Images;

// Whether a raw disk image boots on a stock PC with Secure Boot on. It's judged by the file the firmware starts from
// the EFI system partition.
public enum ImageBootCapability
{
    // Signed under Microsoft's 2011 or 2023 UEFI CA, which PCs trust unless their firmware turns it off.
    SecureBootOk,

    // Not signed, or signed only by a key stock PCs do not trust.
    NotSigned,

    // No EFI system partition, no boot file, or nothing DDT could read.
    Unknown,
}
