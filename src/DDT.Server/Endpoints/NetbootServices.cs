// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Netboot;

namespace DDT.Server.Endpoints;

// What the netboot endpoints work with.
internal readonly record struct NetbootServices(DdtDbContext Database, NetbootHelper Helper, NetbootNeighbourhood Neighbourhood, TimeProvider TimeProvider);
