// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.BootImage;

// A driver package that the last build put into the boot image, as it was at build time.
public sealed record BootImageBuildDriver(Guid PackageId, string Name, string Sha256);
