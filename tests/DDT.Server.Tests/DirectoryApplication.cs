// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Authentication;
using DDT.Server.Ldap;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDT.Server.Tests;

public sealed class DirectoryApplication : DdtApplication
{
    public FakeLdapAuthenticator Ldap { get; } = new();

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("DDT:Ldap:Enabled", "true");
        builder.UseSetting($"DDT:Ldap:GroupRoleMap:{FakeLdapAuthenticator.OperatorsGroup}", DdtRoleNames.Operator);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILdapAuthenticator>();
            services.AddSingleton<ILdapAuthenticator>(Ldap);
        });
    }
}
