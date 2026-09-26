// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// Sent to every connection when machines are removed, so a page drops them from its list without reading it again.
public sealed record MachinesRemovedEvent(IReadOnlyList<Guid> MachineIds);
