// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Configuration;

public sealed class DdtOptions
{
    public const string SectionName = "DDT";

    public string Roles { get; init; } = string.Empty;

    public string StorePath { get; init; } = "/var/lib/ddt";

    public bool RequireHttps { get; init; } = true;

    public HttpsOptions Https { get; init; } = new();
}
