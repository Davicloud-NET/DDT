// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Security;

public sealed class DdtForwardedHeadersOptions
{
    public const string SectionName = "DDT:ForwardedHeaders";

    // Comma separated rather than lists, for the same reason as DDT:Roles.
    public string KnownProxies { get; set; } = string.Empty;

    public string KnownNetworks { get; set; } = string.Empty;
}
