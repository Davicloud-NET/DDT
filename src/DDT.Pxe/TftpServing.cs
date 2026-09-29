// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Protocols.Tftp;

namespace DDT.Pxe;

// How a TftpListener serves: each transfer's limits, how many run at once, and whether they all answer from port 69.
public sealed record TftpServing(TftpLimits Limits, int MaxTransfers, bool SinglePort);
