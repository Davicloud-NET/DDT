// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// The machine's latest Autopilot hardware hash, without the hash. UploadMessage says why an upload failed.
public sealed record MachineHardwareHashView(
    string SerialNumber,
    string? GroupTag,
    DateTimeOffset CollectedUtc,
    HardwareHashUploadState UploadState,
    string? UploadMessage);
