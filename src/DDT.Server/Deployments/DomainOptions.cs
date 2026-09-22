// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// With a name, a sequence's Join the domain step joins the machine to this domain in Windows. The account needs only
// the right to create computer objects in the organizational unit: every Operator can read its password.
public sealed class DomainOptions
{
    public string? Name { get; set; }

    public string? OrganizationalUnit { get; set; }

    // DOMAIN\user or user@suffix, passed to the join whole.
    public string? UserName { get; set; }

    public string? Password { get; set; }
}
