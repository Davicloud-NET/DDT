// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Protocols.Tftp;

public readonly record struct TftpLimits(
    int MaxBlockSize,
    int MaxWindowSize,
    int MaxRetries,
    TimeSpan DefaultTimeout,
    TimeSpan MaxRetransmitDelay)
{
    // A 1380-octet block plus 32 octets of headers fits WireGuard's 1420 MTU, and 1412 over PPPoE. A window of 16
    // blocks measured reliable. Three doubling retries capped at 4 s drop a silent client after 11 s, before EDK2
    // gives up at 15 s.
    public static TftpLimits Default => new(1380, 16, 3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));
}
