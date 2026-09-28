// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Settings;

internal sealed record PxeHostCandidate(string Name, IReadOnlyList<string> Addresses, bool Served);
