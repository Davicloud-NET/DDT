// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// The machines whose hardware hashes go into one CSV file for Intune.
public sealed record AutopilotCsvRequest(IReadOnlyList<Guid> MachineIds);
