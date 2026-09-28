// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.About;

public sealed record AboutInfo(
    string Product,
    string Version,
    string Attribution,
    string License,
    string SourceUrl,
    // The paths that /api/about/legal/{path} serves. They're relative to the legal folder and use forward slashes.
    IReadOnlyList<string> LegalDocuments);
