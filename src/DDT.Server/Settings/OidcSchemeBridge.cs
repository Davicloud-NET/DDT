// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace DDT.Server.Settings;

// The framework owns OpenIdConnectOptions and caches them per scheme. They are configured from the snapshot, and the
// monitor drops its cached value when the snapshot changes, because the change token below fires on every publish.
public sealed class OidcSchemeBridge(DdtSettings settings) : IConfigureNamedOptions<OpenIdConnectOptions>, IOptionsChangeTokenSource<OpenIdConnectOptions>
{
    public string? Name => OidcOptions.SchemeName;

    public IChangeToken GetChangeToken() => settings.GetChangeToken();

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name == OidcOptions.SchemeName)
        {
            OidcSchemeOptions.Configure(options, settings.Current.Oidc);
        }
    }

    public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);
}
