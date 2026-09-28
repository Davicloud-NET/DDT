// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Deployments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Tests;

// The join account from DomainDeploymentApplication, checked at a named domain controller.
// A fake answers for that controller.
public sealed class DomainJoinCheckApplication() : SettingsApplication(
    ("DDT:Deployment:LocalAdministrator:Password", DomainDeploymentApplication.AdministratorPassword),
    ("DDT:Deployment:Domain:Name", "corp.example"),
    ("DDT:Deployment:Domain:OrganizationalUnit", "OU=Workstations,DC=corp,DC=example"),
    ("DDT:Deployment:Domain:UserName", @"CORP\ddt-join"),
    ("DDT:Deployment:Domain:Password", DomainDeploymentApplication.JoinPassword),
    ("DDT:Deployment:Domain:Controller", "dc1.corp.example"))
{
    public FakeDomainDirectory Directory { get; } = new();

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        base.ConfigureTestHost(builder);

        builder.ConfigureServices(services => services.AddSingleton<IDomainDirectory>(Directory));
    }
}
