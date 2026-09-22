// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// Sent to the connections that watch the machine: its log has lines up to LastLineId, to read with after.
public sealed record MachineLogAppendedEvent(Guid MachineId, long LastLineId);
