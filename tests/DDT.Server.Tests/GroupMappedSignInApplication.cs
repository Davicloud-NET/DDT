// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Tests;

// Single sign-on through FakeOidcHandler with DDT:Oidc:AutoProvision on. A map turns the groups claim into roles.
// It has group names like Keycloak and Authentik send them, a group path, and a group object ID like Entra ID sends it.
public sealed class GroupMappedSignInApplication : DdtApplication
{
    public const string Administrators = "ddt-admins";
    public const string Operators = "ddt-operators";
    public const string ViewersPath = "/ddt/viewers";
    public const string EntraOperators = "2f5c3a0e-8a9e-4c5e-9b1a-1c2d3e4f5a6b";

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("DDT:Oidc:AutoProvision", "true");
        builder.UseSetting("DDT:Oidc:AutoProvisionRole", DdtRoleNames.Viewer);
        builder.UseSetting($"DDT:Oidc:GroupRoleMap:{Administrators}", DdtRoleNames.Administrator);
        builder.UseSetting($"DDT:Oidc:GroupRoleMap:{Operators}", DdtRoleNames.Operator);
        builder.UseSetting($"DDT:Oidc:GroupRoleMap:{ViewersPath}", DdtRoleNames.Viewer);
        builder.UseSetting($"DDT:Oidc:GroupRoleMap:{EntraOperators}", DdtRoleNames.Operator);
        builder.ConfigureTestServices(services => services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, FakeOidcHandler>(OidcOptions.SchemeName, configureOptions: null));
    }
}
