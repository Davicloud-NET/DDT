// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.BootImage;

// A build in the boot directory that could be served. Name is null for files copied into the boot directory itself.
public sealed record BootImageStoredBuild(string? Name, DateTimeOffset? BuiltUtc, bool Current);
