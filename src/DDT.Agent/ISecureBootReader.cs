// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Agent;

// What the firmware says about Secure Boot. Each member is null when it can't be told.
public interface ISecureBootReader
{
    // Whether this start had Secure Boot on.
    bool? Enabled();

    // Which of Microsoft's third-party UEFI CAs the firmware's db holds.
    UefiCa? TrustedCas();
}
