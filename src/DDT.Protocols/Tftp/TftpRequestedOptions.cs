// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Protocols.Tftp;

public readonly record struct TftpRequestedOptions(int? BlockSize, int? Timeout, long? TransferSize, int? WindowSize)
{
    public bool Any => BlockSize is not null || Timeout is not null || TransferSize is not null || WindowSize is not null;
}
