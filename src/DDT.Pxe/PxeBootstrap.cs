// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// What only configuration decides for netboot. That's the HTTP boot port and the folder files are served from.
public sealed record PxeBootstrap(int HttpBootPort, BootFileResolver Files);
