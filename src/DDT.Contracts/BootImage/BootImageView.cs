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
    // The boot image must be rebuilt: StaleReasons names why.
    bool Stale,
    // "drivers": the flagged drivers differ from the build's, or drivers are flagged and nothing is built.
    // "serverAddress": the image names the server by a name or port it no longer has. "root": the image trusts
    // another root than the server's. "adk": the server's ADK is another version than the image came from.
    IReadOnlyList<string> StaleReasons,
    BootImageBuilder Builder,
    // The job that runs, or the last one since the server started.
    BootImageJob? Job,
    // What the boot directory holds, newest first.
    IReadOnlyList<BootImageStoredBuild> Builds);
