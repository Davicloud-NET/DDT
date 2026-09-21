// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Protocols.Tftp;

public enum TftpOpcode : ushort
{
    ReadRequest = 1,
    WriteRequest = 2,
    Data = 3,
    Acknowledgement = 4,
    Error = 5,
    OptionAcknowledgement = 6,
}
