// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.SignalR;

namespace DDT.Server.Live;

// Server to client only. Clients receive small change events and patch or refetch their queries.
public sealed class LiveHub : Hub;
