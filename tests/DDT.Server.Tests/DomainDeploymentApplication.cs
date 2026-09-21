// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Tests;

// Machines join a domain and get a local administrator, with the other answer file settings set too.
public sealed class DomainDeploymentApplication() : SettingsApplication(
    ("DDT:Deployment:TimeZone", "W. Europe Standard Time"),
    ("DDT:Deployment:Keyboard", "0407:00000407"),
    ("DDT:Deployment:LocalAdministrator:Password", DomainDeploymentApplication.AdministratorPassword),
    ("DDT:Deployment:Domain:Name", "corp.example"),
    ("DDT:Deployment:Domain:OrganizationalUnit", "OU=Workstations,DC=corp,DC=example"),
    ("DDT:Deployment:Domain:UserName", @"CORP\ddt-join"),
    ("DDT:Deployment:Domain:Password", DomainDeploymentApplication.JoinPassword))
{
    public const string AdministratorPassword = "Local <admin> & 7";

    public const string JoinPassword = "Join \"password\" 7";
}
