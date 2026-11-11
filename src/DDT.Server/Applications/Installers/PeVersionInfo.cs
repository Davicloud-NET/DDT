// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Applications.Installers;

// What a PE file's version resource says about the product.
internal sealed record PeVersionInfo(string? ProductName, string? CompanyName, string? ProductVersion)
{
    public static readonly PeVersionInfo None = new(null, null, null);
}
