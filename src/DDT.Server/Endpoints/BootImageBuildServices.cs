// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.BootImage;
using DDT.Server.Data;
using DDT.Server.Live;

namespace DDT.Server.Endpoints;

// What the boot image's build endpoints work with.
internal readonly record struct BootImageBuildServices(
    DdtDbContext Database,
    BootImageViews Views,
    BootImageJobs Jobs,
    BootImageCatalog Catalog,
    LiveNotifier Live,
    TimeProvider TimeProvider);
