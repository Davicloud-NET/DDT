// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;

namespace DDT.Server.Records;

// The machine's latest Autopilot hardware hash, one row per machine. A new collection replaces it.
public sealed class MachineHardwareHash
{
    public Guid MachineId { get; set; }

    public required string SerialNumber { get; set; }

    public required string Hash { get; set; }

    public string? GroupTag { get; set; }

    public DateTimeOffset CollectedUtc { get; set; }

    public HardwareHashUploadState UploadState { get; set; }

    public string? UploadMessage { get; set; }

    // The import's id in Autopilot, while it is followed.
    public string? ImportId { get; set; }

    // When the import was sent. The uploader gives up on it a while later.
    public DateTimeOffset? UploadStartedUtc { get; set; }
}
