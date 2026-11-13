// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

// The uploaded files of an application, kept as a package the Packages page does not list.
public sealed record ApplicationFiles(Guid PackageId, string Name, long SizeBytes);
