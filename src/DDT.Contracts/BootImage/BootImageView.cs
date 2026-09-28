// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.BootImage;

// The driver packages flagged for the Windows PE boot image, and what the last build put in.
public sealed record BootImageView(
    IReadOnlyList<BootImageDriver> Drivers,
    // SHA-256 as lower case hex over one line per flagged package, "{packageId} {sha256}\n" in the order of the ids;
    // null when none is flagged.
    string? DriverSetHash,
    // Null when the boot directory holds no readable ddt-boot-image.json.
    BootImageBuild? Build,
    // The boot image has to be built again to carry the flagged drivers: the build's set differs, or there is no build
    // to compare with while drivers are flagged.
    bool Stale);
