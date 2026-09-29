// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Protocols.Tftp;
using Microsoft.Extensions.Logging;

namespace DDT.Pxe;

// What every TftpTransfer of a listener shares.
internal sealed record TftpTransferServices(TftpLimits Limits, TimeProvider TimeProvider, ILogger Logger);
