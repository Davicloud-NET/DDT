// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

// Installed when Windows knows the MSI product code, a GUID.
public sealed record ProductCodeDetection(string ProductCode) : ApplicationDetection;
