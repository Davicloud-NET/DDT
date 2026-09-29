// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Contracts.Agents;

// An image that a step of the run uses. The agent downloads it by its Sha256 from AgentRoutes.RunFile.
public sealed record AgentRunImage(
    // The image id that an ApplyImage or WriteRawImage step names.
    Guid ImageId,
    string Name,
    string Sha256,
    // The download size. A raw disk image is compressed with zstd.
    long SizeBytes,
    int WimIndex,
    // For a raw disk image, the size of the disk it holds.
    long InstalledBytes,
    ImageKind Kind = ImageKind.Wim,
    ImageBootCapability? BootCapability = null,
    // Which of Microsoft's third-party UEFI CAs signed the boot file of a raw disk image.
    UefiCa? SignedUnder = null);
