// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// A sequence the machine can run. Either Secure Boot flag makes the agent ask for the override where Secure Boot is on.
public sealed record SequenceOption(
    Guid Id,
    string Name,
    string? Description,
    // The one an assignment rule chose for the machine.
    bool Suggested,
    bool ErasesDisk,
    bool NeedsComputerName,
    // 0 when the run needs no disk space.
    long RequiredBytes,
    // The disk image it writes is not signed for Secure Boot, or DDT cannot tell.
    bool NotSignedForSecureBoot,
    // It is signed only under CAs this machine's firmware does not trust.
    bool NotTrustedHere);
