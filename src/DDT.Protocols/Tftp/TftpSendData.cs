// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Protocols.Tftp;

// Block is the value that goes on the wire, already truncated to 16 bits. FileOffset and Length say
// which bytes the caller must read, so the session never touches the file.
public sealed record TftpSendData(ushort Block, long FileOffset, int Length) : TftpAction;
