// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;

namespace DDT.Contracts.Deployments;

// One run in the history of every machine, with what the list shows of the machine it ran on.
public sealed record RunHistoryItem(
    Guid MachineId,
    // The name an operator or the technician gave the machine, null until someone does.
    string? MachineName,
    string? MachineModel,
    string? Manufacturer,
    string PrimaryMac,
    DeviceKind DeviceKind,
    DeploymentSummary Run);
