// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.CloudInit;

// The rendered seed files; a null NetworkConfig leaves network-config out.
public sealed record CloudInitSeedFiles(string MetaData, string UserData, string? NetworkConfig);
