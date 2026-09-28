// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// OrganizationalUnit: null for the domain's default Computers container.
public sealed record DomainDirectoryRequest(string Controller, string UserName, string Password, string? OrganizationalUnit)
{
    // Leaves out Password, which the generated ToString would print into a log.
    public override string ToString() =>
        $"DomainDirectoryRequest {{ Controller = {Controller}, UserName = {UserName}, OrganizationalUnit = {OrganizationalUnit} }}";
}
