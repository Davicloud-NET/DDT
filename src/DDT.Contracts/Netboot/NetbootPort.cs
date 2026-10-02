// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Netboot;

// One of the UDP ports netboot uses on the server: 67, 69 or 4011, with who holds it.
public sealed record NetbootPort(int Port, IReadOnlyList<NetbootPortOwner> Owners);
