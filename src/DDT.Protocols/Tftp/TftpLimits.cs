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
    // The block size is payload only. On the wire a data packet adds 4 octets of TFTP, 8 of UDP and 20
    // of IP, so a 1400 octet block is a 1432 octet packet and fragments inside WireGuard's default
    // 1420 MTU, which is how DDT's remote sites are joined. 1388 fits 1420; 1380 also fits a tunnel
    // over a PPPoE underlay, where the MTU is 1412. Four is the only window size with Microsoft
    // backing for bootmgr.
    //
    // Three retries at a doubling delay capped at four seconds abandons a silent client eleven
    // seconds after the last acknowledgement. That has to land clearly inside the fifteen to twenty
    // four seconds EDK2 waits before giving up on the server, so DDT frees the session first rather
    // than holding it open for a machine that has already moved on.
    public static TftpLimits Default => new(1380, 4, 3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));
}
