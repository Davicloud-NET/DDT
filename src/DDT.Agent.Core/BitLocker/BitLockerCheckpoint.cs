// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;

namespace DDT.Agent.BitLocker;

// What an Enable BitLocker step did over its turns: the content of its checkpoint. Never the password. ProtectorId is
// the recovery password's protector, Confirmed says the server has its password, and Policy names the policy values
// the step set for an AD backup that may have been cut off.
public sealed class BitLockerCheckpoint
{
    public bool FeatureEnabled { get; set; }

    public Guid? ProtectorId { get; set; }

    public bool Confirmed { get; set; }

    public List<string> Policy { get; set; } = [];

    public bool BackedUp { get; set; }

    // A checkpoint that cannot be read counts as none.
    public static BitLockerCheckpoint Read(string? saved)
    {
        try
        {
            return (string.IsNullOrEmpty(saved) ? null : JsonSerializer.Deserialize(saved, BitLockerCheckpointJsonContext.Default.BitLockerCheckpoint))
                ?? new BitLockerCheckpoint();
        }
        catch (JsonException)
        {
            return new BitLockerCheckpoint();
        }
    }

    public string Write() => JsonSerializer.Serialize(this, BitLockerCheckpointJsonContext.Default.BitLockerCheckpoint);
}
