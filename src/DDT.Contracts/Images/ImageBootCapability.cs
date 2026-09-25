// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Images;

// Whether a raw disk image starts on a stock PC with Secure Boot on, judged by the file firmware starts from its EFI
// system partition.
public enum ImageBootCapability
{
    // Signed under Microsoft's UEFI CA, 2011 or 2023, which stock PCs trust.
    SecureBootOk,

    // Not signed, or signed only by a key stock PCs do not trust.
    NotSigned,

    // No EFI system partition, no boot file, or nothing DDT could read.
    Unknown,
}
