// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;

namespace DDT.Contracts.Packages;

// BootImage adds a driver package to the Windows PE boot image, or takes it out; null leaves it as it is, so a client
// that does not know the setting keeps it.
public sealed record UpdatePackageRequest(string Name, string? Description, IReadOnlyList<HardwareModel> Targets, bool? BootImage = null);
