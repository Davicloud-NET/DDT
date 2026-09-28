// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Contracts.Agents;

// An image a step of the run uses, which the agent downloads by its Sha256 from AgentRoutes.RunFile.
public sealed record AgentRunImage(
    // The one an ApplyImage or WriteRawImage step names.
    Guid ImageId,
    string Name,
    string Sha256,
    // What is downloaded; a raw disk image is compressed with zstd.
    long SizeBytes,
    int WimIndex,
    // For a raw disk image, the size of the disk it holds.
    long InstalledBytes,
    ImageKind Kind = ImageKind.Wim,
    ImageBootCapability? BootCapability = null,
    // Which of Microsoft's third-party UEFI CAs a raw disk image's boot file is signed under.
    UefiCa? SignedUnder = null);
