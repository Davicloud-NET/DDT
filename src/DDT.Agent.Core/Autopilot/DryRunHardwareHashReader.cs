// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Autopilot;

// Reads nothing, and answers a client edition with a made-up serial number and a hash of Windows' length.
public sealed class DryRunHardwareHashReader(AgentLog log) : IHardwareHashReader
{
    public const string SerialNumber = "DRY-RUN-0001";

    // 3,000 bytes, which base64 makes the 4,000 characters Windows reports.
    public static readonly string Hash = Convert.ToBase64String(new byte[3000]);

    public Task<HardwareHashReading> ReadAsync(CancellationToken cancellationToken)
    {
        log.Information($"Dry run: no hardware hash is read; serial number {SerialNumber} and a made-up hash stand in.");

        return Task.FromResult(new HardwareHashReading(HardwareHashReading.Client, SerialNumber, Hash));
    }
}
