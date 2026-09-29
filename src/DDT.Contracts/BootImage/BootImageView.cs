// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.BootImage;

// The driver packages flagged for the Windows PE boot image, and what the last build put in.
public sealed record BootImageView(
    IReadOnlyList<BootImageDriver> Drivers,
    // SHA-256 in lower case hex over one line per flagged package, "{packageId} {sha256}\n", sorted by id. Null when
    // no package is flagged.
    string? DriverSetHash,
    // Null when the boot directory holds no readable ddt-boot-image.json.
    BootImageBuild? Build,
    // The boot image must be rebuilt to include the flagged drivers. That's the case when the build's driver set
    // differs, or when drivers are flagged and there's no build to compare with.
    bool Stale);
