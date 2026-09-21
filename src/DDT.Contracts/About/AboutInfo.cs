// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.About;

// LegalDocuments: the paths /api/about/legal/{path} serves, relative to the legal folder, with forward slashes.
public sealed record AboutInfo(
    string Product,
    string Version,
    string Attribution,
    string License,
    string SourceUrl,
    IReadOnlyList<string> LegalDocuments);
