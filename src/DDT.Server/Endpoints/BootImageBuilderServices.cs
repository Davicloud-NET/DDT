// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.BootImage;
using DDT.Server.Data;

namespace DDT.Server.Endpoints;

// What the builder's endpoints work with.
internal readonly record struct BootImageBuilderServices(
    DdtDbContext Database,
    BootImageViews Views,
    BuilderTokens Tokens,
    BuilderPackage Package,
    IServiceProvider Services,
    TimeProvider TimeProvider);
