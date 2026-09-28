// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Images;

public static class ImageUploadLimits
{
    // Well under the default body limits of Kestrel and IIS. nginx needs its limit raised for any chunk size.
    public const int ChunkBytes = 8 * 1024 * 1024;

    public const int MaxFileNameLength = 256;

    public const int RetryAfterSeconds = 5;

    // Room kept free on the store volume for the database and the logs, on top of the other uploads' remaining bytes.
    public const long FreeSpaceMargin = 1024L * 1024 * 1024;

    // Kestrel averages its minimum data rate over the whole body. Without this timeout, a chunk that stops half way
    // could hold its session's lock for hours.
    public static readonly TimeSpan NoProgressTimeout = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);
}
