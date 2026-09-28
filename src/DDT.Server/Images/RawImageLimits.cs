// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Images;

public static class RawImageLimits
{
    public const int MaxBootDetailLength = 512;

    // zstd level 9 compresses a cloud image at hundreds of MB/s on several cores. The result is within 15 % of level
    // 19's size, and level 19 takes ten times as long.
    public const int CompressionLevel = 9;

    // The number of threads that compress a large image. The same disk uploaded again is found by the SHA-256 of the
    // disk, not of its compressed copy.
    public const int CompressionWorkers = 4;
}
