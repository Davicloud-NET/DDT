// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Disks;

namespace DDT.Server.Images;

// A disk image turned into what the library stores: CompressedPath holds the raw disk compressed with zstd, Sha256
// and SizeBytes describe that file, SourceSha256 and Info the raw disk. Refusal says why the upload cannot be used
// instead, and then nothing else is set.
public sealed record RawImport(
    string? Refusal,
    string CompressedPath = "",
    string Sha256 = "",
    long SizeBytes = 0,
    string SourceSha256 = "",
    RawImageInfo? Info = null);
