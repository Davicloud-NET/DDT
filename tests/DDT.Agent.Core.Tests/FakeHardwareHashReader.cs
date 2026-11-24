// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Autopilot;

namespace DDT.Agent.Tests;

// Answers Reading, and counts how often it was asked.
internal sealed class FakeHardwareHashReader : IHardwareHashReader
{
    public HardwareHashReading Reading { get; set; } = new(HardwareHashReading.Client, "SN-1", Convert.ToBase64String(new byte[3000]));

    public int Reads { get; private set; }

    public Task<HardwareHashReading> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult(Reading);
    }
}
