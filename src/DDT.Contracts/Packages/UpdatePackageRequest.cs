// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;

namespace DDT.Contracts.Packages;

public sealed record UpdatePackageRequest(string Name, string? Description, IReadOnlyList<HardwareModel> Targets);
