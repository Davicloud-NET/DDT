// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Configuration;

public sealed class DdtOptions
{
    public const string SectionName = "DDT";

    public string Roles { get; set; } = string.Empty;

    public string StorePath { get; set; } = "/var/lib/ddt";

    // Sqlite, PostgreSql or SqlServer. Unset, a connection string means PostgreSql and none means Sqlite.
    public string Database { get; set; } = string.Empty;

    public bool RequireHttps { get; set; } = true;

    // Folders on the server that images and MDT deployment shares are imported from, besides "import" in the store.
    public string[] ImportFolders { get; set; } = [];

    public HttpsOptions Https { get; set; } = new();
}
