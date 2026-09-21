// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Tests;

// New accounts on, and FakeOidcHandler under the scheme name of the OpenID Connect handler, which DDT does not
// register while DDT:Oidc:Enabled is off.
public sealed class ExternalSignInApplication : DdtApplication
{
    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("DDT:Oidc:AutoProvision", "true");
        builder.UseSetting("DDT:Oidc:AutoProvisionRole", DdtRoleNames.Operator);
        builder.ConfigureTestServices(services => services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, FakeOidcHandler>(OidcOptions.SchemeName, configureOptions: null));
    }
}
