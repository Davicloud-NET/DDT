// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.BootImage;

// What a builder's token says: which download it came with, whose it was, and until when it uploads.
public sealed record BuilderToken(Guid Id, string IssuedBy, DateTimeOffset ExpiresUtc);
