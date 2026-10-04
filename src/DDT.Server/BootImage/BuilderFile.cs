// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.BootImage;

// builder.json in the builder: where Build-BootImage.ps1 sends what it built, and the token that lets it.
public sealed record BuilderFile(string ServerUrl, string UploadToken, DateTimeOffset ExpiresUtc);
