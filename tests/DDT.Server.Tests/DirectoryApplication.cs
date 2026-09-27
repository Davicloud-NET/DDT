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

// Directory sign-in against FakeLdapAuthenticator, whose groups the map gives each role, and a mapped group the
// directory no longer has.
public class DirectoryApplication : DdtApplication
{
    public const string Host = "dc.corp.example";

    public const string BaseDn = "dc=corp,dc=example";

    public FakeLdapAuthenticator Ldap { get; } = new();

    // Without a map, administrators give directory accounts their roles.
    protected virtual bool MapsGroups => true;

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("DDT:Ldap:Enabled", "true");
        builder.UseSetting("DDT:Ldap:Host", Host);
        builder.UseSetting("DDT:Ldap:BaseDn", BaseDn);

        if (MapsGroups)
        {
            builder.UseSetting($"DDT:Ldap:GroupRoleMap:{FakeLdapAuthenticator.OperatorsGroup}", DdtRoleNames.Operator);
            builder.UseSetting($"DDT:Ldap:GroupRoleMap:{FakeLdapAuthenticator.AdministratorsGroup}", DdtRoleNames.Administrator);
            builder.UseSetting($"DDT:Ldap:GroupRoleMap:{FakeLdapAuthenticator.ViewersGroup}", DdtRoleNames.Viewer);
            builder.UseSetting($"DDT:Ldap:GroupRoleMap:{FakeLdapAuthenticator.RetiredGroup}", DdtRoleNames.Viewer);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILdapAuthenticator>();
            services.AddSingleton<ILdapAuthenticator>(Ldap);
        });
    }
}

public sealed class UnmappedDirectoryApplication : DirectoryApplication
{
    protected override bool MapsGroups => false;
}
