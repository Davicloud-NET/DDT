// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.BootImage;

// A build of the boot image, or an install of the ADK, that the server runs or ran last. One runs at a time.
public sealed record BootImageJob(
    BootImageJobKind Kind,
    BootImageJobState State,
    DateTimeOffset StartedUtc,
    DateTimeOffset? FinishedUtc,
    string StartedBy,
    // What stopped it, for a job that failed.
    string? Problem,
    // How many lines of output there are so far. GET /api/boot-image/job has them.
    int Lines);
