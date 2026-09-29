// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// Sent to the connections that watch the machine. Its log now has lines up to LastLineId, which a page reads with the
// after parameter.
public sealed record MachineLogAppendedEvent(Guid MachineId, long LastLineId);
