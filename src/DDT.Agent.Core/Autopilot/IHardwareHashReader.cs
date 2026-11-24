// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Autopilot;

// Reads the running Windows' edition and, on a client edition, its serial number and hardware hash in one go. Any
// failure throws DeploymentStepException.
public interface IHardwareHashReader
{
    Task<HardwareHashReading> ReadAsync(CancellationToken cancellationToken);
}
