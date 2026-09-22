// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

// The token a running agent keeps on disk to continue its run after a restart. ExpiresUtc is checked against the
// server's TimeProvider rather than by a time-limited protector, which reads the system clock.
public sealed record RunTokenPayload(Guid MachineId, Guid RunId, int TokenGeneration, DateTimeOffset ExpiresUtc);
