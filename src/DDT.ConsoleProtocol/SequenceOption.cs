// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// A sequence the machine can run. Suggested marks the one an assignment rule chose for it. RequiredBytes is the disk
// space the run needs, 0 when it needs none. NotSignedForSecureBoot says that the disk image it writes is not signed for
// Secure Boot, or that DDT cannot tell; NotTrustedHere that it is signed only under CAs this machine's firmware does not
// trust while Secure Boot is on. Either way the agent asks for the Secure Boot override before it runs where Secure Boot
// is on.
public sealed record SequenceOption(
    Guid Id,
    string Name,
    string? Description,
    bool Suggested,
    bool ErasesDisk,
    bool NeedsComputerName,
    long RequiredBytes,
    bool NotSignedForSecureBoot,
    bool NotTrustedHere);
