// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The logo the console at the machine shows at the right end of its top bar. Null values when there is none. An upload
// answers with it as well.
public sealed record ConsoleLogoView(
    string? Sha256,
    long? Size,
    int? Width,
    int? Height,
    DateTimeOffset? UploadedUtc,
    string? UploadedBy);
