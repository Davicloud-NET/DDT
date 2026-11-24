// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// What the machine's runs handed the server: its recovery keys, newest first, and its hardware hash.
public sealed record MachineRecordsView(Guid MachineId, IReadOnlyList<RecoveryKeyView> RecoveryKeys, MachineHardwareHashView? HardwareHash);
