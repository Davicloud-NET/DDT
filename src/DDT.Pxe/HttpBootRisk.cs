// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// Why a machine's HTTP reads could meet what its start before left on the server.
public enum HttpBootRisk
{
    None,

    // A read ended before its whole answer was sent, less than three minutes ago.
    ReadEndedBadly,

    // The server's system still holds a connection between the machine and the boot port.
    ConnectionLeft,
}
