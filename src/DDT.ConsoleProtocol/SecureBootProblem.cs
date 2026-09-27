// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// Why a disk image would not start with the Secure Boot the machine has on.
public enum SecureBootProblem
{
    // Its boot file is not signed for Secure Boot. It starts once Secure Boot is off, or with your own key enrolled.
    NotSigned,

    // DDT cannot tell whether its boot file is signed for Secure Boot.
    MayNotStart,

    // Its boot file is signed only under CAs the firmware does not trust. It starts once one of them is allowed, or
    // Secure Boot is off.
    UntrustedCa,
}
