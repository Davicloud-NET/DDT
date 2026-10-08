// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;

namespace DDT.Pxe;

// Who asks for a boot file and on which of the server's addresses. A generated file names that address.
public sealed record BootRequest(IPAddress Server, IPAddress Client);
