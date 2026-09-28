// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Server.Tests;

// The disk a seeded raw image holds: its size once written, and the CA its boot loader is signed under, where that is
// not Microsoft's third-party CA 2011.
public sealed record RawImageDisk(long InstalledBytes = 16L * 1024 * 1024, UefiCa? SignedUnder = null);
