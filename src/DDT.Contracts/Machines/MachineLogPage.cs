// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// Lines in the order they arrived. HasOlder says whether there are lines before the first one, to page back with the
// before parameter.
public sealed record MachineLogPage(IReadOnlyList<MachineLogEntry> Lines, bool HasOlder);
