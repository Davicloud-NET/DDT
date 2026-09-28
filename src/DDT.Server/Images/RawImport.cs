// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Core.Disks;

namespace DDT.Server.Images;

// CompressedPath holds the raw disk compressed with zstd, which Sha256 and SizeBytes describe; SourceSha256 and Info
// describe the raw disk. Refusal is set instead, and Retryable says its cause lies with the server, such as a missing
// tool or a full volume, so the upload is kept.
public sealed record RawImport(
    ServerMessage? Refusal,
    string CompressedPath = "",
    string Sha256 = "",
    long SizeBytes = 0,
    string SourceSha256 = "",
    RawImageInfo? Info = null,
    bool Retryable = false);
