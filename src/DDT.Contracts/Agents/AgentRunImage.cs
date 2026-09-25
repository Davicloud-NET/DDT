// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Contracts.Agents;

// ImageId is the one an ApplyImage or WriteRawImage step names. The agent downloads the file by its Sha256 from
// AgentRoutes.RunFile. For a raw disk image, the file is compressed with zstd, SizeBytes is what is downloaded,
// InstalledBytes the disk it holds, and BootCapability whether it starts with Secure Boot on.
public sealed record AgentRunImage(
    Guid ImageId,
    string Name,
    string Sha256,
    long SizeBytes,
    int WimIndex,
    long InstalledBytes,
    ImageKind Kind = ImageKind.Wim,
    ImageBootCapability? BootCapability = null);
